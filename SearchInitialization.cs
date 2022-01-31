// Copyright © 2022 Jeroen Stemerdink.
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
    using EPiServer;
    using EPiServer.Core;
    using EPiServer.Framework;
    using EPiServer.Framework.Initialization;
    using EPiServer.Logging;
    using EPiServer.ServiceLocation;

    /// <summary>
    ///     Class SearchInitialization.
    /// </summary>
    [InitializableModule]
    [ModuleDependency(typeof(FrameworkInitialization))]
    public class SearchInitialization : IInitializableModule
    {
        /// <summary>
        /// Gets or sets the logger
        /// </summary>
        /// <value>The logger.</value>
        protected readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Gets or sets the content events.
        /// </summary>
        /// <value>The content events.</value>
        protected IContentEvents ContentEvents { get; set; }

        /// <summary>
        /// Gets or sets the helper.
        /// </summary>
        /// <value>The helper.</value>
        protected Helper Helper { get; set; }

        /// <summary>
        ///     Initializes this instance.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <remarks>
        ///     Gets called as part of the EPiServer Framework initialization sequence. Note that it will be called
        ///     only once per AppDomain, unless the method throws an exception. If an exception is thrown, the initialization
        ///     method will be called repeatedly for each request reaching the site until the method succeeds.
        /// </remarks>
        public void Initialize(InitializationEngine context)
        {
            if (context == null)
            {
                return;
            }

            this.ContentEvents = context.Locate.Advanced.GetInstance<IContentEvents>();
            
            this.Helper = new Helper(serviceProvider: context.Locate.Advanced);
            
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

            this.Helper.UpdateParents(contentLink: contentEventArgs.ContentLink);
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

            // Check if the content that is published is a page. If it's not, don't do anything.
            if (!(contentEventArgs.Content is PageData pageData))
            {
                return;
            }

            if (pageData.IsReadOnly)
            {
                pageData = pageData.CreateWritableClone();
                contentEventArgs.Content = pageData;
            }

            this.Helper.UpdateAdditionalSearchContent(parent: pageData);
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
    }
}