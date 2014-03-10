﻿namespace ParTech.Modules.SeoUrl.Pipelines
{
    using System;
    using System.Web;
    using Sitecore;
    using Sitecore.Collections;
    using Sitecore.Data.Items;
    using Sitecore.Links;
    using Sitecore.Pipelines.HttpRequest;
    using ParTechProviders = ParTech.Modules.SeoUrl.Providers;

    /// <summary>
    /// HttpRequest processor that enables SEO-friendly URL's generated by the LinkProvider to be resolved to Sitecore items.
    /// </summary>
    public class ItemResolver : HttpRequestProcessor
    {
        /// <summary>
        /// Processes the specified pipeline arguments.
        /// </summary>
        /// <param name="args">The args.</param>
        public override void Process(HttpRequestArgs args)
        {
            // If there was a file found on disk for the current request, don't resolve an item
            if (Context.Page != null && !string.IsNullOrWhiteSpace(Context.Page.FilePath))
            {
                return;
            }

            // Only process if we are not using the Core database (which means we are requesting parts of Sitecore admin)
            if (Context.Database == null || Context.Database.Name.Equals("core", StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            // Only continue if Sitecore has not found an item yet
            if (args != null && !string.IsNullOrEmpty(args.Url.ItemPath) && Context.Item == null)
            {
                string path = MainUtil.DecodeName(args.Url.ItemPath);

                // Resolve the item based on the requested path
                Context.Item = this.ResolveItem(path);
            }

            // If the item was not requested using its SEO-friendly URL, 301 redirect to force friendly URL
            if (Context.Item != null && Context.PageMode.IsNormal)
            {
                var provider = LinkManager.Provider as ParTechProviders.LinkProvider;
                if (provider != null && provider.ForceFriendlyUrl)
                {
                    this.ForceFriendlyUrl();
                }
            }
        }

        /// <summary>
        /// Resolve the item with specified path by traversing the Sitecore tree
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private Item ResolveItem(string path)
        {
            bool resolveComplete = false;

            // Only continue if the requested item belongs to the current site
            if (string.IsNullOrEmpty(Context.Site.RootPath) || !path.StartsWith(Context.Site.RootPath, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            // Strip website's rootpath from item path
            path = path.Remove(0, Context.Site.RootPath.Length);

            // Start searching from the site root
            string resolvedPath = Context.Site.RootPath;
            string[] itemNames = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < itemNames.Length; i++)
            {
                string itemName = itemNames[i];

                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    Item child = this.FindChild(resolvedPath, ParTechProviders.LinkProvider.Normalize(itemName));

                    if (child != null)
                    {
                        resolvedPath = child.Paths.FullPath;
                        resolveComplete = i == itemNames.Length - 1;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Only return an item if we completely resolved the requested path
            if (resolveComplete)
            {
                return Context.Database.GetItem(resolvedPath);
            }

            return null;
        }

        /// <summary>
        /// Search the children of parentPath for one that matched the normalized item name
        /// </summary>
        /// <param name="parentPath"></param>
        /// <param name="normalizedItemName"></param>
        /// <returns></returns>
        private Item FindChild(string parentPath, string normalizedItemName)
        {
            Item result = null;

            if (!string.IsNullOrWhiteSpace(parentPath))
            {
                ChildList children = Context.Database.GetItem(parentPath).Children;

                foreach (Item child in children)
                {
                    if (ParTechProviders.LinkProvider.Normalize(child.Name).Equals(normalizedItemName, StringComparison.InvariantCultureIgnoreCase)
                        || ParTechProviders.LinkProvider.Normalize(child.DisplayName).Equals(normalizedItemName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        result = child;
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Force items to be requested using their SEO-friendly URL (by 301 redirecting )
        /// </summary>
        private void ForceFriendlyUrl()
        {
            // Only apply for GET requests
            if (HttpContext.Current.Request.HttpMethod.Equals("get", StringComparison.InvariantCultureIgnoreCase))
            {
                string requestedPath = ParTechProviders.LinkProvider.ToRelativeUrl(HttpContext.Current.Request.Url.AbsolutePath);
                string friendlyPath = ParTechProviders.LinkProvider.ToRelativeUrl(LinkManager.GetItemUrl(Context.Item));

                if (requestedPath != friendlyPath)
                {
                    // Redirect to the SEO-friendly URL
                    string friendlyUrl = string.Concat(LinkManager.GetItemUrl(Context.Item), HttpContext.Current.Request.Url.Query);

                    this.Redirect301(friendlyUrl);
                }
            }
        }

        /// <summary>
        /// Redirect to a URL using a 301 Moved Permanently header
        /// </summary>
        /// <param name="url"></param>
        private void Redirect301(string url)
        {
            var context = HttpContext.Current;

            context.Response.StatusCode = 301;
            context.Response.Status = "301 Moved Permanently";
            context.Response.CacheControl = "no-cache";
            context.Response.AddHeader("Location", url);
            context.Response.AddHeader("Pragma", "no-cache");
            context.Response.Expires = -1;

            context.Response.End();
        }
    }
}