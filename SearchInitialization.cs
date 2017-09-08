// Copyright © 2017 Jeroen Stemerdink.
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
namespace EPi.Libraries.BlockSearch
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text;

    using EPi.Libraries.BlockSearch.DataAnnotations;

    using EPiServer;
    using EPiServer.Core;
    using EPiServer.Core.Html;
    using EPiServer.DataAbstraction;
    using EPiServer.DataAccess;
    using EPiServer.Framework;
    using EPiServer.Framework.Initialization;
    using EPiServer.Logging;
    using EPiServer.Security;
    using EPiServer.ServiceLocation;
    using EPiServer.SpecializedProperties;

    /// <summary>
    ///     Class SearchInitialization.
    /// </summary>
    [InitializableModule]
    [ModuleDependency(typeof(FrameworkInitialization))]
    public class SearchInitialization : IInitializableModule
    {
        /// <summary>
        /// The parent update triggered
        /// </summary>
        private bool parentUpdateTriggered;

        /// <summary>
        /// Gets or sets the logger
        /// </summary>
        /// <value>The logger.</value>
        protected ILogger Logger { get; set; }

        /// <summary>
        /// Gets or sets the content events.
        /// </summary>
        /// <value>The content events.</value>
        protected IContentEvents ContentEvents { get; set; }

        /// <summary>
        /// Gets or sets the content repository.
        /// </summary>
        /// <value>The content repository.</value>
        protected IContentRepository ContentRepository { get; set; }

        /// <summary>
        /// Gets or sets the content soft link repository.
        /// </summary>
        /// <value>The content soft link repository.</value>
        protected IContentSoftLinkRepository ContentSoftLinkRepository { get; set; }

        /// <summary>
        ///     Gets or sets the content type respository.
        /// </summary>
        /// <value>The content type respository.</value>
        protected IContentTypeRepository ContentTypeRepository { get; set; }

        /// <summary>
        ///     Initializes this instance.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <remarks>
        ///     Gets called as part of the EPiServer Framework initialization sequence. Note that it will be called
        ///     only once per AppDomain, unless the method throws an exception. If an exception is thrown, the initialization
        ///     method will be called repeadetly for each request reaching the site until the method succeeds.
        /// </remarks>
        /// <exception cref="ActivationException">if there is are errors resolving the service instance.</exception>
        public void Initialize(InitializationEngine context)
        {
            if (context == null)
            {
                return;
            }

            this.Logger = context.Locate.Advanced.GetInstance<ILogger>();
            this.ContentEvents = context.Locate.Advanced.GetInstance<IContentEvents>();
            this.ContentTypeRepository = context.Locate.Advanced.GetInstance<IContentTypeRepository>();
            this.ContentSoftLinkRepository = context.Locate.Advanced.GetInstance<IContentSoftLinkRepository>();
            this.ContentRepository = context.Locate.Advanced.GetInstance<IContentRepository>();
            
            this.ContentEvents.PublishedContent += this.OnPublishedContent;
            this.ContentEvents.PublishingContent += this.OnPublishingContent;

            this.Logger.Information("[Blocksearch] Initialized.");
        }

        /// <summary>
        ///     Handles the <see cref="E:PublishedContent" /> event.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="contentEventArgs">The <see cref="ContentEventArgs" /> instance containing the event data.</param>
        public void OnPublishedContent(object sender, ContentEventArgs contentEventArgs)
        {
            if (contentEventArgs == null)
            {
                return;
            }

            // Check if the content that is published is indeed a block.
            BlockData blockData = contentEventArgs.Content as BlockData;

            // If it's not, don't do anything.
            if (blockData == null)
            {
                return;
            }

            this.UpdateParents(contentEventArgs.ContentLink);
        }

        /// <summary>
        /// Handles the <see cref="E:PublishingContent" /> event.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="contentEventArgs">The <see cref="ContentEventArgs"/> instance containing the event data.</param>
        public void OnPublishingContent(object sender, ContentEventArgs contentEventArgs)
        {
            if (contentEventArgs == null)
            {
                return;
            }

            // Check if the content that is published is a page.
            PageData pageData = contentEventArgs.Content as PageData;

            // If it's not, don't do anything.
            if (pageData == null)
            {
                return;
            }

            // If a SaveAction.ForceCurrentVersion is triggered on the page itself, publishing content is triggered only once. When initiated from somewhere else, it goed into an endless loop.
            if (this.parentUpdateTriggered)
            {
                return;
            }

            this.UpdateAdditionalSearchContent(parent: pageData);
        }

        /// <summary>
        ///     Resets the module into an uninitialized state.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <remarks>
        ///     <para>
        ///         This method is usually not called when running under a web application since the web app may be shut down very
        ///         abruptly, but your module should still implement it properly since it will make integration and unit testing
        ///         much simpler.
        ///     </para>
        ///     <para>
        ///         Any work done by
        ///         <see
        ///             cref="M:EPiServer.Framework.IInitializableModule.Initialize(EPiServer.Framework.Initialization.InitializationEngine)" />
        ///         as well as any code executing on
        ///         <see cref="E:EPiServer.Framework.Initialization.InitializationEngine.InitComplete" /> should be reversed.
        ///     </para>
        /// </remarks>
        public void Uninitialize(InitializationEngine context)
        {
            this.ContentEvents.PublishedContent -= this.OnPublishedContent;
            this.ContentEvents.PublishingContent -= this.OnPublishingContent;

            this.Logger.Information("[Blocksearch] Uninitialized.");
        }

        /// <summary>
        /// Updates the parents.
        /// </summary>
        /// <param name="contentLink">The content link.</param>
        private void UpdateParents(ContentReference contentLink)
        {
            // Get the references to this block
            List<ContentReference> referencingContentLinks =
                this.ContentSoftLinkRepository.Load(contentLink: contentLink, reversed: true)
                    .Where(
                        link =>
                        (link.SoftLinkType == ReferenceType.PageLinkReference)
                        && !ContentReference.IsNullOrEmpty(contentLink: link.OwnerContentLink))
                    .Select(link => link.OwnerContentLink)
                    .ToList();

            // Loop through each reference
            foreach (ContentReference referencingContentLink in referencingContentLinks)
            {
                PageData parent;
                this.ContentRepository.TryGet(contentLink: referencingContentLink, content: out parent);

                // If it is not pagedata, do nothing
                if (parent == null)
                {
                    this.Logger.Information("[Blocksearch] Referencing content is not a page. Skipping update.");
                    continue;
                }

                // Check if the containing page is published.
                if (!parent.CheckPublishedStatus(status: PagePublishedStatus.Published))
                {
                    this.Logger.Information("[Blocksearch] page named '{0}' is not published. Skipping update.", parent.Name);
                    continue;
                }

                // Republish the containing page.
                try
                {
                    this.ContentRepository.Save(
                            content: parent.CreateWritableClone(),
                            action: SaveAction.Publish | SaveAction.ForceCurrentVersion,
                            access: AccessLevel.NoAccess);
                }
                catch (AccessDeniedException accessDeniedException)
                {
                    this.Logger.Error(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "[Blocksearch] Not enough accessrights to republish containing pagetype named '{0}'.",
                            parent.Name),
                        exception: accessDeniedException);
                }
            }
        }

        /// <summary>
        /// Republishes the parent.
        /// </summary>
        /// <param name="parent">The parent.</param>
        private void UpdateAdditionalSearchContent(PageData parent)
        {
            PropertyInfo addtionalSearchContentProperty = this.GetAddtionalSearchContentProperty(page: parent);

            if (addtionalSearchContentProperty == null)
            {
                return;
            }

            if (addtionalSearchContentProperty.PropertyType != typeof(string))
            {
                return;
            }

            StringBuilder stringBuilder = new StringBuilder();

            ContentType contentType = this.ContentTypeRepository.Load(id: parent.ContentTypeID);

            foreach (PropertyDefinition current in
                from d in contentType.PropertyDefinitions
                where typeof(PropertyContentArea).IsAssignableFrom(c: d.Type.DefinitionType)
                select d)
            {
                PropertyData propertyData = parent.Property[name: current.Name];

                ContentArea contentArea = propertyData.Value as ContentArea;

                if (contentArea == null)
                {
                    continue;
                }

                stringBuilder.Append(this.GetAdditionalContent(contentArea: contentArea));
            }

            if (addtionalSearchContentProperty.PropertyType != typeof(string))
            {
                return;
            }

            try
            {
                string additionalSearchContent = TextIndexer.StripHtml(stringBuilder.ToString(), 0);

                // When being "delayed published" the pagedata is readonly. Create a writable clone to be safe.
                PageData editablePage = parent.CreateWritableClone();
                editablePage[index: addtionalSearchContentProperty.Name] = additionalSearchContent;

                this.parentUpdateTriggered = true;

                this.ContentRepository.Save(
                    content: editablePage,
                    action: SaveAction.ForceCurrentVersion,
                    access: AccessLevel.NoAccess);
            }
            catch (EPiServerException epiServerException)
            {
                this.Logger.Error(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "[Blocksearch] Property {0} does not exist on {1}.",
                        addtionalSearchContentProperty.Name,
                        parent.Name),
                    exception: epiServerException);
            }
        }

        /// <summary>
        ///     Gets the name of the key word property.
        /// </summary>
        /// <param name="page">The page.</param>
        /// <returns>The propertyinfo.</returns>
        private PropertyInfo GetAddtionalSearchContentProperty(PageData page)
        {
            PropertyInfo keywordsMetatagProperty =
                page.GetType().GetProperties().FirstOrDefault(predicate: this.HasAttribute<AdditionalSearchContentAttribute>);

            return keywordsMetatagProperty;
        }

        /// <summary>
        /// Gets the additional search content from the <paramref name="contentArea"/>.
        /// </summary>
        /// <param name="contentArea">The content area.</param>
        /// <returns>The additional search content.</returns>
        private string GetAdditionalContent(ContentArea contentArea)
        {
            StringBuilder stringBuilder = new StringBuilder();

            foreach (ContentAreaItem contentAreaItem in contentArea.Items)
            {
                IContent content;
                if (!this.ContentRepository.TryGet(contentLink: contentAreaItem.ContentLink, content: out content))
                {
                    continue;
                }

                // content area item can be null when duplicating a page
                if (content == null)
                {
                    continue;
                }

                // Check if the content is indeed a block, and not a page used in a content area
                BlockData blockData = content as BlockData;

                // Content area is not a block, but probably a page used as a teaser.
                if (blockData == null)
                {
                    this.Logger.Information(
                        "[Blocksearch] Contentarea item is not block data. Skipping update.",
                        content.Name);
                    continue;
                }

                IEnumerable<string> props = this.GetSearchablePropertyValues(contentData: content, contentTypeId: content.ContentTypeID);
                stringBuilder.AppendFormat(" {0}", string.Join(" ", values: props));
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        ///     Determines whether the specified self has attribute.
        /// </summary>
        /// <typeparam name="T">The type of the attribute.</typeparam>
        /// <param name="memberInfo">The memberInfo.</param>
        /// <returns><c>true</c> if the specified self has attribute; otherwise, <c>false</c>.</returns>
        private bool HasAttribute<T>(MemberInfo memberInfo) where T : Attribute
        {
            T attr = default(T);

            try
            {
                attr = (T)Attribute.GetCustomAttribute(element: memberInfo, attributeType: typeof(T));
            }
            catch (Exception exception)
            {
                this.Logger.Error("[Blocksearch] Error getting custom attribute.", exception: exception);
            }

            return attr != null;
        }

        /// <summary>
        ///     Gets the searchable property values.
        /// </summary>
        /// <param name="contentData">The content data.</param>
        /// <param name="contentType">Type of the content.</param>
        /// <returns>A list of prperty values.</returns>
        private IEnumerable<string> GetSearchablePropertyValues(IContentData contentData, ContentType contentType)
        {
            if (contentType == null)
            {
                yield break;
            }

            foreach (PropertyDefinition current in
                from d in contentType.PropertyDefinitions
                where d.Searchable || typeof(IPropertyBlock).IsAssignableFrom(c: d.Type.DefinitionType)
                select d)
            {
                PropertyData propertyData = contentData.Property[name: current.Name];
                IPropertyBlock propertyBlock = propertyData as IPropertyBlock;
                if (propertyBlock != null)
                {
                    foreach (string current2 in
                        this.GetSearchablePropertyValues(
                            contentData: propertyBlock.Block,
                            contentTypeId: propertyBlock.BlockPropertyDefinitionTypeID))
                    {
                        yield return current2;
                    }
                }
                else
                {
                    yield return propertyData.ToWebString();
                }
            }
        }

        /// <summary>
        ///     Gets the searchable property values.
        /// </summary>
        /// <param name="contentData">The content data.</param>
        /// <param name="contentTypeId">The content type identifier.</param>
        /// <returns>A list of searchable property values.</returns>
        private IEnumerable<string> GetSearchablePropertyValues(IContentData contentData, int contentTypeId)
        {
            return this.GetSearchablePropertyValues(contentData: contentData, contentType: this.ContentTypeRepository.Load(id: contentTypeId));
        }
    }
}