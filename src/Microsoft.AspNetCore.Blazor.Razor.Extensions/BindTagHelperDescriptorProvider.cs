// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor;

namespace Microsoft.AspNetCore.Blazor.Razor
{
    internal class BindTagHelperDescriptorProvider : ITagHelperDescriptorProvider
    {
        // Run after the component tag helper provider, because we need to see the results.
        public int Order { get; set; } = 1000;

        public RazorEngine Engine { get; set; }

        public void Execute(TagHelperDescriptorProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // This provider returns tag helper information for 'bind' which doesn't necessarily
            // map to any real component. Bind behaviors more like a macro, which can map a single LValue to
            // both a 'value' attribute and a 'value changed' attribute.
            //
            // User types: 
            //      <input type="text" bind="@FirstName"/>
            //
            // We generate:
            //      <input type="text" 
            //          value="@BindMethods.GetValue(FirstName)" 
            //          onchange="@BindMethods.SetValue(__value => FirstName = __value, FirstName)"/>
            //
            // This isn't very different from code the user could write themselves - thus the pronouncement
            // that bind is very much like a macro.
            //
            // A lot of the value that provide in this case is that the associations between the
            // elements, and the attributes aren't straightforward.
            //
            // For instance on <input type="text" /> we need to listen to 'value' and 'onchange',
            // but on <input type="checked" we need to listen to 'checked' and 'onchange'.
            //
            // We handle a few different cases here:
            //
            //  1.  When given an attribute like 'bind-value-changed="@FirstName"' we will generate the
            //      'value' attribute and 'changed' attribute. 
            //
            //      We don't do any transformation or inference for this case, because the developer has
            //      told us exactly what to do. This is the *full* form of bind, and should support any
            //      combination of elemement, component, and attributes.
            //
            //  2.  When given an attribute like 'bind-value="@FirstName"' we will generate the 'value'
            //      attribute and 'valuechanged' attribute - UNLESS we know better. For instance, with
            //      input tags we know that 'valuechanged' is likely not correct, the correct attribute
            //      is 'onchange
            //
            //      We will have to build up a list of mappings that describe the right thing to do for
            //      specific cases. Again, this is where we add substantial values to app developers. These
            //      kinds of things in the DOM aren't consistent, but presenting a uniform experience that
            //      generally does the right thing, we will make Blazor accessible to those without much
            //      experience doing DOM programming.
            //
            //  3.  When given an attribute like 'bind="@FirstName"' we will generate a value and change
            //      attribute solely based on the context. We need the context of an HTML tag to know
            //      what attributes to generate.
            //
            //      Similar to case #2, this should 'just work' from the users point of view. We expect
            //      using this syntax most frequently with input elements.
            //
            //  4.  For components, we have a bit of a special case. We can infer a syntax that matches
            //      case #2 based on property names. So if a component provides both 'Value' and 'ValueChanged'
            //      we will turn that into an instance of bind.
            //
            // So case #1 here is the most general case. Case #2 and #3 are data-driven based on element data
            // we have. Case #4 is data-driven based on component definitions.
            var compilation = context.GetCompilation();
            if (compilation == null)
            {
                return;
            }

            var bindMethods = compilation.GetTypeByMetadataName(BlazorApi.BindMethods.FullTypeName);
            if (bindMethods == null)
            {
                // If we can't find BindMethods, then just bail. We won't be able to compile the
                // generated code anyway.
                return;
            }

            // Tag Helper defintion for case #1. This is the most general case.
            context.Results.Add(CreateFallbackBindTagHelper());

            // For case #2 & #3 we have a whole bunch of attribute entries on BindMethods that we can use
            // to data-drive the definitions of these tag helpers.
            var elementBindData = GetElementBindData(compilation, bindMethods.GetAttributes());

            // Case #2
            foreach (var tagHelper in CreateElementBindValueTagHelpers(elementBindData))
            {
                context.Results.Add(tagHelper);
            }

            // Case #3
            foreach (var tagHelper in CreateElementBindTagHelpers(elementBindData))
            {
                context.Results.Add(tagHelper);
            }

            // For case #4 we look at the tag helpers that were already created corresponding to components
            // and pattern match on properties.
            foreach (var tagHelper in CreateComponentBindTagHelpers(context.Results))
            {
                context.Results.Add(tagHelper);
            }
        }

        private TagHelperDescriptor CreateFallbackBindTagHelper()
        {
            var builder = TagHelperDescriptorBuilder.Create(BlazorMetadata.Bind.TagHelperKind, "Bind", BlazorApi.AssemblyName);
            builder.Documentation = Resources.BindTagHelper_Fallback_Documentation;

            builder.Metadata.Add(BlazorMetadata.SpecialKindKey, BlazorMetadata.Bind.TagHelperKind);
            builder.Metadata[TagHelperMetadata.Runtime.Name] = BlazorMetadata.Bind.RuntimeName;

            // WTE has a bug in 15.7p1 where a Tag Helper without a display-name that looks like
            // a C# property will crash trying to create the toolips.
            builder.SetTypeName("Microsoft.AspNetCore.Blazor.Bind");

            builder.TagMatchingRule(rule =>
            {
                rule.TagName = "*";
                rule.Attribute(attribute =>
                {
                    attribute.Name = "bind-";
                    attribute.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.PrefixMatch;
                });
            });

            builder.BindAttribute(attribute =>
            {
                attribute.Documentation = Resources.BindTagHelper_Fallback_Documentation;

                attribute.Name = "bind-...";
                attribute.AsDictionary("bind-", typeof(object).FullName);
                attribute.TypeName = typeof(IDictionary<string, object>).FullName;

                // WTE has a bug 15.7p1 where a Tag Helper without a display-name that looks like
                // a C# property will crash trying to create the toolips.
                attribute.SetPropertyName("Value");
            });

            return builder.Build();
        }

        private List<ElementBindData> GetElementBindData(Compilation compilation, ImmutableArray<AttributeData> attributes)
        {
            var bindElement = compilation.GetTypeByMetadataName(BlazorApi.BindElementAttribute.FullTypeName);
            var bindInputElement = compilation.GetTypeByMetadataName(BlazorApi.BindInputElementAttribute.FullTypeName);

            var results = new List<ElementBindData>();

            for (var i = 0; i < attributes.Length; i++)
            {
                var attribute = attributes[i];

                if (attribute.AttributeClass == bindElement)
                {
                    results.Add(new ElementBindData(
                        (string)attribute.ConstructorArguments[0].Value,
                        null,
                        (string)attribute.ConstructorArguments[1].Value,
                        (string)attribute.ConstructorArguments[2].Value,
                        (string)attribute.ConstructorArguments[3].Value));
                }
                else if (attribute.AttributeClass == bindInputElement)
                {
                    results.Add(new ElementBindData(
                        "input",
                        (string)attribute.ConstructorArguments[0].Value,
                        (string)attribute.ConstructorArguments[1].Value,
                        (string)attribute.ConstructorArguments[2].Value,
                        (string)attribute.ConstructorArguments[3].Value));
                }
            }

            return results;
        }

        private List<TagHelperDescriptor> CreateElementBindValueTagHelpers(List<ElementBindData> data)
        {
            var results = new List<TagHelperDescriptor>();

            for (var i = 0; i < data.Count; i++)
            {
                var entry = data[i];

                var builder = TagHelperDescriptorBuilder.Create(BlazorMetadata.Bind.TagHelperKind, "Bind-" + entry.Element, BlazorApi.AssemblyName);
                builder.DisplayName = "Bind";
                builder.Documentation = "Bind-Documentation";

                builder.Metadata.Add(BlazorMetadata.SpecialKindKey, BlazorMetadata.Bind.TagHelperKind);
                builder.Metadata[TagHelperMetadata.Runtime.Name] = BlazorMetadata.Bind.RuntimeName;
                builder.Metadata[BlazorMetadata.Bind.ValueAttribute] = entry.Value;
                builder.Metadata[BlazorMetadata.Bind.ChangeHandlerAttribute] = entry.ChangeHandler;

                // WTE has a bug in 15.7p1 where a Tag Helper without a display-name that looks like
                // a C# property will crash trying to create the toolips.
                builder.SetTypeName("Microsoft.AspNetCore.Blazor.Bind");

                builder.TagMatchingRule(rule =>
                {
                    rule.TagName = entry.Element;
                    if (entry.Type != null)
                    {
                        rule.Attribute(a =>
                        {
                            a.Name = "type";
                            a.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.FullMatch;
                            a.Value = entry.Type;
                            a.ValueComparisonMode = RequiredAttributeDescriptor.ValueComparisonMode.FullMatch;
                        });
                    }

                    rule.Attribute(a =>
                    {
                        a.Name = "bind-" + entry.Value;
                        a.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.FullMatch;
                    });
                });

                builder.BindAttribute(a =>
                {
                    a.Documentation = "Bind-Documentation2";
                    a.Name = "bind-" + entry.Value;
                    a.TypeName = typeof(IDictionary<string, object>).FullName;

                    // WTE has a bug 15.7p1 where a Tag Helper without a display-name that looks like
                    // a C# property will crash trying to create the toolips.
                    a.SetPropertyName("Bind");
                });

                results.Add(builder.Build());
            }

            return results;
        }

        private List<TagHelperDescriptor> CreateElementBindTagHelpers(List<ElementBindData> data)
        {
            var results = new List<TagHelperDescriptor>();

            // For this case we're trying to create 'bind' tag helpers, where there's a unique value
            // for that element that we want to expose using bind.
            //
            // For instance if the 'foo' tag has two values that we can bind to, then don't create an
            // instance of 'bind' because it would be ambiguous.
            var unique = data.GroupBy(e => (e.Element, e.Type)).Where(g => g.Count() == 1).ToArray();

            for (var i = 0; i < unique.Length; i++)
            {
                var entry = unique[i].Single();

                var builder = TagHelperDescriptorBuilder.Create(BlazorMetadata.Bind.TagHelperKind, "Bind-" + entry.Element, BlazorApi.AssemblyName);
                builder.DisplayName = "Bind";
                builder.Documentation = "Bind-Documentation";

                builder.Metadata.Add(BlazorMetadata.SpecialKindKey, BlazorMetadata.Bind.TagHelperKind);
                builder.Metadata[TagHelperMetadata.Runtime.Name] = BlazorMetadata.Bind.RuntimeName;
                builder.Metadata[BlazorMetadata.Bind.ValueAttribute] = entry.Value;
                builder.Metadata[BlazorMetadata.Bind.ChangeHandlerAttribute] = entry.ChangeHandler;

                // WTE has a bug in 15.7p1 where a Tag Helper without a display-name that looks like
                // a C# property will crash trying to create the toolips.
                builder.SetTypeName("Microsoft.AspNetCore.Blazor.Bind");

                builder.TagMatchingRule(rule =>
                {
                    rule.TagName = entry.Element;
                    if (entry.Type != null)
                    {
                        rule.Attribute(a =>
                        {
                            a.Name = "type";
                            a.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.FullMatch;
                            a.Value = entry.Type;
                            a.ValueComparisonMode = RequiredAttributeDescriptor.ValueComparisonMode.FullMatch;
                        });
                    }

                    rule.Attribute(a =>
                    {
                        a.Name = "bind";
                        a.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.FullMatch;
                    });
                });

                builder.BindAttribute(a =>
                {
                    a.Documentation = "Bind-Documentation2";
                    a.Name = "bind";
                    a.TypeName = typeof(IDictionary<string, object>).FullName;

                    // WTE has a bug 15.7p1 where a Tag Helper without a display-name that looks like
                    // a C# property will crash trying to create the toolips.
                    a.SetPropertyName("Bind");
                });

                results.Add(builder.Build());
            }

            return results;
        }

        private List<TagHelperDescriptor> CreateComponentBindTagHelpers(ICollection<TagHelperDescriptor> tagHelpers)
        {
            var results = new List<TagHelperDescriptor>();

            foreach (var tagHelper in tagHelpers)
            {
                if (!tagHelper.IsComponentTagHelper())
                {
                    continue;
                }

                // We want to create a 'bind' tag helper everywhere we see a pair of properties like `Foo`, `FooChanged`
                // where `FooChanged` is a delegate and `Foo` is not.
                //
                // The easiest way to figure this out without a lot of backtracking is to look for `FooChanged` and then
                // try to find a matching "Foo".
                for (var i = 0; i < tagHelper.BoundAttributes.Count; i++)
                {
                    var changeHandlerAttribute = tagHelper.BoundAttributes[i];
                    if (!changeHandlerAttribute.Name.EndsWith("Changed") || !changeHandlerAttribute.IsDelegateProperty())
                    {
                        continue;
                    }

                    BoundAttributeDescriptor valueAttribute = null;
                    var valueAttributeName = changeHandlerAttribute.Name.Substring(0, changeHandlerAttribute.Name.Length - "Changed".Length);
                    for (var j = 0; j < tagHelper.BoundAttributes.Count; j++)
                    {
                        if (tagHelper.BoundAttributes[j].Name == valueAttributeName && !tagHelper.BoundAttributes[j].IsDelegateProperty())
                        {
                            valueAttribute = tagHelper.BoundAttributes[j];
                            break;
                        }
                    }

                    if (valueAttribute == null)
                    {
                        // No matching attribute found.
                        continue;
                    }

                    var builder = TagHelperDescriptorBuilder.Create(BlazorMetadata.Bind.TagHelperKind, tagHelper.Name, tagHelper.AssemblyName);
                    builder.DisplayName = "Bind";
                    builder.Documentation = "Bind-Documentation";

                    builder.Metadata.Add(BlazorMetadata.SpecialKindKey, BlazorMetadata.Bind.TagHelperKind);
                    builder.Metadata[TagHelperMetadata.Runtime.Name] = BlazorMetadata.Bind.RuntimeName;
                    builder.Metadata[BlazorMetadata.Bind.ValueAttribute] = valueAttribute.Name;
                    builder.Metadata[BlazorMetadata.Bind.ChangeHandlerAttribute] = changeHandlerAttribute.Name;

                    // WTE has a bug 15.7p1 where a Tag Helper without a display-name that looks like
                    // a C# property will crash trying to create the toolips.
                    builder.SetTypeName(tagHelper.GetTypeName());

                    // Match the component and attribute name
                    builder.TagMatchingRule(rule =>
                    {
                        rule.TagName = tagHelper.TagMatchingRules.Single().TagName;
                        rule.Attribute(attribute =>
                        {
                            attribute.Name = "bind-" + valueAttribute.Name;
                            attribute.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.FullMatch;
                        });
                    });

                    builder.BindAttribute(attribute =>
                    {
                        attribute.Documentation = "Bind-Documentation2";
                        attribute.Name = "bind-" + valueAttribute.Name;
                        attribute.TypeName = valueAttribute.TypeName;
                        attribute.IsEnum = valueAttribute.IsEnum;

                            // WTE has a bug 15.7p1 where a Tag Helper without a display-name that looks like
                            // a C# property will crash trying to create the toolips.
                            attribute.SetPropertyName(valueAttribute.GetPropertyName());
                    });

                    results.Add(builder.Build());
                }
            }

            return results;
        }

        private struct ElementBindData
        {
            public ElementBindData(string element, string type, string suffix, string value, string changeHandler)
            {
                Element = element;
                Type = type;
                Suffix = suffix;
                Value = value;
                ChangeHandler = changeHandler;
            }

            public string Element { get; }
            public string Type { get; }
            public string Suffix { get; }
            public string Value { get; }
            public string ChangeHandler { get; }
        }
    }
}
