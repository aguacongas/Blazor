// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System;
using Microsoft.AspNetCore.Razor.Language;

namespace Microsoft.AspNetCore.Blazor.Razor
{
    internal static class TagHelperDescriptorExtensions
    {
        public static bool IsBindTagHelper(this TagHelperDescriptor tagHelper)
        {
            if (tagHelper == null)
            {
                throw new ArgumentNullException(nameof(tagHelper));
            }
            
            return 
                tagHelper.Metadata.TryGetValue(BlazorMetadata.SpecialKindKey, out var value) && 
                string.Equals(BlazorMetadata.Bind.TagHelperKind, value);
        }

        public static bool IsFallbackBindTagHelper(this TagHelperDescriptor tagHelper)
        {
            if (tagHelper == null)
            {
                throw new ArgumentNullException(nameof(tagHelper));
            }

            return
                tagHelper.Metadata.TryGetValue(BlazorMetadata.SpecialKindKey, out var value) &&
                string.Equals(BlazorMetadata.Bind.TagHelperKind, value) &&
                string.Equals("Bind", tagHelper.Name);
        }

        public static string GetValueAttributeName(this TagHelperDescriptor tagHelper)
        {
            if (tagHelper == null)
            {
                throw new ArgumentNullException(nameof(tagHelper));
            }

            tagHelper.Metadata.TryGetValue(BlazorMetadata.Bind.ValueAttribute, out var result);
            return result;
        }

        public static string GetChangeHandlerAttributeName(this TagHelperDescriptor tagHelper)
        {
            if (tagHelper == null)
            {
                throw new ArgumentNullException(nameof(tagHelper));
            }

            tagHelper.Metadata.TryGetValue(BlazorMetadata.Bind.ChangeHandlerAttribute, out var result);
            return result;
        }

        public static bool IsComponentTagHelper(this TagHelperDescriptor tagHelper)
        {
            if (tagHelper == null)
            {
                throw new ArgumentNullException(nameof(tagHelper));
            }

            return !tagHelper.Metadata.ContainsKey(BlazorMetadata.SpecialKindKey);
        }
    }
}
