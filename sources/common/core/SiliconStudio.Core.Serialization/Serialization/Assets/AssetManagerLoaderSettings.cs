﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Linq;
using SiliconStudio.Core.Serialization.Contents;

namespace SiliconStudio.Core.Serialization.Assets
{
    /// <summary>
    /// Specifies settings for <see cref="AssetManager.Load{T}"/> operations.
    /// </summary>
    public sealed class AssetManagerLoaderSettings
    {
        private static readonly AssetManagerLoaderSettings defaultValue = new AssetManagerLoaderSettings();
        private static readonly AssetManagerLoaderSettings ignoreReferences = new AssetManagerLoaderSettings { LoadContentReferences = false };
        private bool loadContentReferences = true;

        public delegate void ContentFilterDelegate(ContentReference contentReference, ref bool shouldBeLoaded);

        /// <summary>
        /// Gets the default loader settings.
        /// </summary>
        /// <value>
        /// The default loader settings.
        /// </value>
        public static AssetManagerLoaderSettings Default
        {
            get { return defaultValue; }
        }

        /// <summary>
        /// Gets the loader settings which doesn't load content references.
        /// </summary>
        /// <value>
        /// The loader settings which doesn't load content references.
        /// </value>
        public static AssetManagerLoaderSettings IgnoreReferences
        {
            get { return ignoreReferences; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="ContentReference{T}"/> should be loaded.
        /// </summary>
        /// <value>
        ///   <c>true</c> if <see cref="ContentReference{T}"/> should be loaded; otherwise, <c>false</c>.
        /// </value>
        public bool LoadContentReferences
        {
            get { return loadContentReferences; }
            set { loadContentReferences = value; }
        }

        /// <summary>
        /// Gets or sets a filter that can indicate whether <see cref="ContentReference{T}"/> should be loaded.
        /// </summary>
        /// <value>
        /// The content reference filter.
        /// </value>
        public ContentFilterDelegate ContentFilter { get; set; }

        /// <summary>
        /// Creates a new content filter that won't load chunk if not one of the given types.
        /// </summary>
        /// <param name="types">The accepted types.</param>
        /// <returns></returns>
        public static ContentFilterDelegate NewContentFilterByType(params Type[] types)
        {
            // We could convert to HashSet, but usually not worth it for small sets
            return (ContentReference contentReference, ref bool shouldBeLoaded) =>
            {
                if (!types.Contains(contentReference.Type))
                    shouldBeLoaded = false;
            };
        }
    }
}