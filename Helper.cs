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
    using EPiServer.Logging;
    using EPiServer.Security;
    using EPiServer.ServiceLocation;
    using EPiServer.SpecializedProperties;

    /// <summary>
    /// Class Helper.
    /// </summary>
    public class Helper
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Helper"/> class.
        /// </summary>
        /// <param name="contentRepository">The content repository.</param>
        /// <param name="contentSoftLinkRepository">The content soft link repository.</param>
        /// <param name="contentTypeRepository">The content type repository.</param>
        /// <param name="logger">The logger.</param>
        public Helper(IContentRepository contentRepository, IContentSoftLinkRepository contentSoftLinkRepository, IContentTypeRepository contentTypeRepository, ILogger logger)
        {
            this.ContentRepository = contentRepository;
            this.ContentSoftLinkRepository = contentSoftLinkRepository;
            this.ContentTypeRepository = contentTypeRepository;
            this.Logger = logger;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Helper" /> class.
        /// </summary>
        /// <param name="serviceLocator">The service locator.</param>
        /// <exception cref="ActivationException">if there is are errors resolving  the service instance.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="serviceLocator"/> is <see langword="null"/></exception>
        public Helper(IServiceLocator serviceLocator)
        {
            if (serviceLocator == null)
            {
                throw new ArgumentNullException(nameof(serviceLocator));
            }

            this.ContentRepository = serviceLocator.GetInstance<IContentRepository>();
            this.ContentSoftLinkRepository = serviceLocator.GetInstance<IContentSoftLinkRepository>();
            this.ContentTypeRepository = serviceLocator.GetInstance<IContentTypeRepository>();
            this.Logger = serviceLocator.GetInstance<ILogger>();
        }

        /// <summary>
        /// Gets the logger
        /// </summary>
        /// <value>The logger.</value>
        private ILogger Logger { get; }

        /// <summary>
        /// Gets the content repository.
        /// </summary>
        /// <value>The content repository.</value>
        private IContentRepository ContentRepository { get; }

        /// <summary>
        /// Gets the content soft link repository.
        /// </summary>
        /// <value>The content soft link repository.</value>
        private IContentSoftLinkRepository ContentSoftLinkRepository { get; }

        /// <summary>
        ///     Gets the content type respository.
        /// </summary>
        /// <value>The content type respository.</value>
        private IContentTypeRepository ContentTypeRepository { get; }

        /// <summary>
        /// Updates the parents.
        /// </summary>
        /// <param name="contentLink">The content link.</param>
        public void UpdateParents(ContentReference contentLink)
        {
            // Get the references to this block
            List<ContentReference> referencingContentLinks = this.ContentSoftLinkRepository.Load(contentLink: contentLink, reversed: true)
                    .Where(
                        link =>
                        link.SoftLinkType == ReferenceType.PageLinkReference
                        && !ContentReference.IsNullOrEmpty(contentLink: link.OwnerContentLink))
                    .Select(link => link.OwnerContentLink)
                    .ToList();

            // Loop through each reference
            foreach (ContentReference referencingContentLink in referencingContentLinks)
            {
                this.ContentRepository.TryGet(contentLink: referencingContentLink, content: out PageData parent);

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
                            parent.CreateWritableClone(),
                            SaveAction.Publish | SaveAction.ForceCurrentVersion,
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
        public void UpdateAdditionalSearchContent(PageData parent)
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

            foreach (PropertyDefinition current in from d in contentType.PropertyDefinitions
                                                   where typeof(PropertyContentArea).IsAssignableFrom(
                                                       c: d.Type.DefinitionType)
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

                parent[index: addtionalSearchContentProperty.Name] = additionalSearchContent;
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
        /// Gets the additional search content from the <paramref name="contentArea"/>.
        /// </summary>
        /// <param name="contentArea">The content area.</param>
        /// <returns>The additional search content.</returns>
        private string GetAdditionalContent(ContentArea contentArea)
        {
            StringBuilder stringBuilder = new StringBuilder();

            foreach (ContentAreaItem contentAreaItem in contentArea.Items)
            {
                if (!this.ContentRepository.TryGet(contentLink: contentAreaItem.ContentLink, content: out IContent content))
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

                IEnumerable<string> props = this.GetSearchablePropertyValues(
                    contentData: content,
                    contentTypeId: content.ContentTypeID);
                stringBuilder.AppendFormat(" {0}", string.Join(" ", values: props));
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        ///     Gets the name of the key word property.
        /// </summary>
        /// <param name="page">The page.</param>
        /// <returns>The propertyinfo.</returns>
        private PropertyInfo GetAddtionalSearchContentProperty(PageData page)
        {
            PropertyInfo keywordsMetatagProperty = page.GetType().GetProperties()
                .FirstOrDefault(predicate: this.HasAttribute<AdditionalSearchContentAttribute>);

            return keywordsMetatagProperty;
        }

        /// <summary>
        ///     Gets the searchable property values.
        /// </summary>
        /// <param name="contentData">The content data.</param>
        /// <param name="contentType">Type of the content.</param>
        /// <returns>A list of prperty values.</returns>
        private IEnumerable<string> GetSearchablePropertyValues(
            IContentData contentData,
            ContentType contentType)
        {
            if (contentType == null)
            {
                yield break;
            }

            foreach (PropertyDefinition current in from d in contentType.PropertyDefinitions
                                                   where d.Searchable
                                                         || typeof(IPropertyBlock).IsAssignableFrom(
                                                             c: d.Type.DefinitionType)
                                                   select d)
            {
                PropertyData propertyData = contentData.Property[name: current.Name];

                if (propertyData is IPropertyBlock propertyBlock)
                {
                    foreach (string current2 in this.GetSearchablePropertyValues(
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
            return this.GetSearchablePropertyValues(
                contentData: contentData,
                contentType: this.ContentTypeRepository.Load(id: contentTypeId));
        }

        /// <summary>
        ///     Determines whether the specified self has attribute.
        /// </summary>
        /// <typeparam name="T">The type of the attribute.</typeparam>
        /// <param name="memberInfo">The memberInfo.</param>
        /// <returns><c>true</c> if the specified self has attribute; otherwise, <c>false</c>.</returns>
        private bool HasAttribute<T>(MemberInfo memberInfo)
            where T : Attribute
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
    }
}