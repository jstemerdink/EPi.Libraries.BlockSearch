// Copyright © 2026 Jeroen Stemerdink.
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
    using DataAnnotations;
    using EPiServer;
    using EPiServer.Core;
    using EPiServer.DataAbstraction;
    using EPiServer.DataAccess;
    using EPiServer.HtmlParsing;
    using EPiServer.Security;
    using EPiServer.SpecializedProperties;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;

    /// <summary>
    /// Class Helper.
    /// </summary>
    public class Helper
    {
        private readonly ILogger<Helper> _logger;
        private readonly IContentRepository _contentRepository;
        private readonly IContentSoftLinkRepository _contentSoftLinkRepository;
        private readonly IContentTypeRepository _contentTypeRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="Helper" /> class.
        /// </summary>
        /// <param name="contentRepository">The content repository.</param>
        /// <param name="contentSoftLinkRepository">The content soft link repository.</param>
        /// <param name="contentTypeRepository">The content type repository.</param>
        /// <param name="logger">The logger.</param>
        public Helper(IContentRepository contentRepository, IContentSoftLinkRepository contentSoftLinkRepository, IContentTypeRepository contentTypeRepository, ILogger<Helper> logger)
        {
            _contentRepository = contentRepository;
            _contentSoftLinkRepository = contentSoftLinkRepository;
            _contentTypeRepository = contentTypeRepository;
            _logger = logger;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Helper" /> class.
        /// </summary>
        /// <param name="serviceProvider">The service locator.</param>
        /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <see langword="null"/></exception>
        public Helper(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            _contentRepository = serviceProvider.GetRequiredService<IContentRepository>();
            _contentSoftLinkRepository = serviceProvider.GetRequiredService<IContentSoftLinkRepository>();
            _contentTypeRepository = serviceProvider.GetRequiredService<IContentTypeRepository>();
            _logger = serviceProvider.GetRequiredService<ILogger<Helper>>();
        }
        
        /// <summary>
        /// Updates the parents.
        /// </summary>
        /// <param name="contentLink">The content link.</param>
        public void UpdateParents(ContentReference contentLink)
        {
            // Get the references to this block
            List<ContentReference> referencingContentLinks = _contentSoftLinkRepository.Load(contentLink: contentLink, reversed: true)
                    .Where(
                        link =>
                        link.SoftLinkType == ReferenceType.PageLinkReference
                        && !ContentReference.IsNullOrEmpty(contentLink: link.OwnerContentLink))
                    .Select(link => link.OwnerContentLink)
                    .ToList();

            // Loop through each reference
            foreach (ContentReference referencingContentLink in referencingContentLinks)
            {
                _contentRepository.TryGet(contentLink: referencingContentLink, content: out PageData parent);

                // If it is not page data, do nothing
                if (parent == null)
                {
                    _logger.LogInformation("[Blocksearch] Referencing content is not a page. Skipping update");
                    continue;
                }

                // Check if the containing page is published.
                if (!parent.CheckPublishedStatus(status: PagePublishedStatus.Published))
                {
                    _logger.LogInformation("[Blocksearch] page named '{ParentName}' is not published. Skipping update", parent.Name);
                    continue;
                }

                // Republish the containing page.
                try
                {
                    _contentRepository.Save(
                            parent.CreateWritableClone(),
                            SaveAction.Publish | SaveAction.ForceCurrentVersion | SaveAction.SkipValidation,
                            access: AccessLevel.NoAccess);
                }
                catch (AccessDeniedException accessDeniedException)
                {
                    _logger.LogError(accessDeniedException, "[Blocksearch] Not enough access rights to republish containing page type named '{ParentName}'", parent.Name);
                }
            }
        }

        /// <summary>
        /// Republishes the parent.
        /// </summary>
        /// <param name="parent">The parent.</param>
        public void UpdateAdditionalSearchContent(PageData parent)
        {
            PropertyInfo additionalSearchContentProperty = GetAdditionalSearchContentProperty(page: parent);

            if (additionalSearchContentProperty == null)
            {
                return;
            }

            if (additionalSearchContentProperty.PropertyType != typeof(string))
            {
                return;
            }

            StringBuilder stringBuilder = new();

            ContentType contentType = _contentTypeRepository.Load(id: parent.ContentTypeID);

            foreach (PropertyDefinition current in from d in contentType.PropertyDefinitions
                                                   where typeof(PropertyContentArea).IsAssignableFrom(
                                                       c: d.Type.DefinitionType)
                                                   select d)
            {
                PropertyData propertyData = parent.Property[name: current.Name];


                if (propertyData.Value is not ContentArea contentArea)
                {
                    continue;
                }

                stringBuilder.Append(GetAdditionalContent(contentArea: contentArea));
            }

            if (additionalSearchContentProperty.PropertyType != typeof(string))
            {
                return;
            }

            try
            {
                HtmlFilter htmlFilter = new(FilterRules.StripHtml);

                StringBuilder filteredOutput = new();
                StringWriter outputWriter = new(filteredOutput);

                htmlFilter.FilterHtml(new StringReader(stringBuilder.ToString()), outputWriter);
                outputWriter.Dispose();
                
                string additionalSearchContent = filteredOutput.ToString();
                
                parent[index: additionalSearchContentProperty.Name] = additionalSearchContent;

                outputWriter.Dispose();
            }
            catch (EPiServerException epiServerException)
            {
                _logger.LogError(epiServerException, "[Blocksearch] Property {PropertyName} does not exist on {ParentName}", additionalSearchContentProperty.Name,
                    parent.Name);
            }
        }

        /// <summary>
        /// Gets the additional search content from the <paramref name="contentArea"/>.
        /// </summary>
        /// <param name="contentArea">The content area.</param>
        /// <returns>The additional search content.</returns>
        private string GetAdditionalContent(ContentArea contentArea)
        {
            StringBuilder stringBuilder = new();

            foreach (ContentAreaItem contentAreaItem in contentArea.Items)
            {
                if (!_contentRepository.TryGet(contentLink: contentAreaItem.ContentLink, content: out IContent content))
                {
                    continue;
                }

                // content area item can be null when duplicating a page
                if (content == null)
                {
                    continue;
                }

                // Check if the content is indeed a block, and not a page used in a content area

                // Content area is not a block, but probably a page used as a teaser.
                if (content is not BlockData)
                {
                    _logger.LogInformation("[Blocksearch] Content area item {ContentName} is not block data. Skipping update", content.Name);
                    continue;
                }

                IEnumerable<string> props = GetSearchablePropertyValues(content, content.ContentTypeID);
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture, " {0}", string.Join(" ", values: props));
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        ///     Gets the name of the key word property.
        /// </summary>
        /// <param name="page">The page.</param>
        /// <returns>The property info.</returns>
        private PropertyInfo GetAdditionalSearchContentProperty(PageData page)
        {
            PropertyInfo keywordsMetaTagProperty = page.GetType().GetProperties()
                .FirstOrDefault(predicate: HasAttribute<AdditionalSearchContentAttribute>);

            return keywordsMetaTagProperty;
        }

        /// <summary>
        ///     Gets the searchable property values.
        /// </summary>
        /// <param name="contentData">The content data.</param>
        /// <param name="contentType">Type of the content.</param>
        /// <returns>A list of pr0perty values.</returns>
        private IEnumerable<string> GetSearchablePropertyValues(
            IContentData contentData,
            ContentType contentType)
        {
            if (contentType == null)
            {
                yield break;
            }

            var definitions = from d in contentType.PropertyDefinitions
                where d.IndexingType == IndexingType.Searchable ||
                      typeof(IPropertyBlock).IsAssignableFrom(c: d.Type.DefinitionType)
                select d;

            foreach (PropertyDefinition current in definitions)
            {
                PropertyData propertyData = contentData.Property[name: current.Name];

                if (propertyData is IPropertyBlock propertyBlock)
                {
                    foreach (string propertyValues in GetSearchablePropertyValues(
                        propertyBlock.Block,
                        propertyBlock.ItemTypeReference.GUID))
                    {
                        yield return propertyValues;
                    }
                }
                else
                {
                    yield return propertyData.ToWebString();
                }
            }
        }

        /// <summary>
        /// Gets the searchable property values.
        /// </summary>
        /// <param name="contentData">The content data.</param>
        /// <param name="contentTypeGuid">The content type unique identifier.</param>
        /// <returns>A list of searchable property values.</returns>
        private IEnumerable<string> GetSearchablePropertyValues(IContentData contentData, Guid contentTypeGuid)
        {
            return GetSearchablePropertyValues(
                contentData: contentData,
                contentType: _contentTypeRepository.Load(contentTypeGuid));
        }

        /// <summary>
        /// Gets the searchable property values.
        /// </summary>
        /// <param name="contentData">The content data.</param>
        /// <param name="contentTypeId">The content type identifier.</param>
        /// <returns>A list of searchable property values.</returns>
        private IEnumerable<string> GetSearchablePropertyValues(IContentData contentData, int contentTypeId)
        {
            return GetSearchablePropertyValues(
                contentData: contentData,
                contentType: _contentTypeRepository.Load(contentTypeId));
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
            T attr = null;

            try
            {
                attr = (T)Attribute.GetCustomAttribute(element: memberInfo, attributeType: typeof(T));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "[Blocksearch] Error getting custom attribute");
            }

            return attr != null;
        }
    }
}