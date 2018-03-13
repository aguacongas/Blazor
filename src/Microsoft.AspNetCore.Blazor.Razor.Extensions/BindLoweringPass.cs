// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Microsoft.AspNetCore.Blazor.Razor
{
    internal class BindLoweringPass : IntermediateNodePassBase, IRazorOptimizationPass
    {
        protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
        {
            var @namespace = documentNode.FindPrimaryNamespace();
            var @class = documentNode.FindPrimaryClass();
            if (@namespace == null || @class == null)
            {
                // Nothing to do, bail. We can't function without the standard structure.
                return;
            }

            // For each bind *usage* we need to rewrite the tag helper node to map to basic contstructs.
            var nodes = documentNode.FindDescendantNodes<TagHelperIntermediateNode>();
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];

                // Reverse order because we will remove nodes.
                for (var j = node.Children.Count - 1; j >= 0; j--)
                {
                    // For each usage of the 'fallback' bind tag helper, it could duplicate
                    // the usage of a more specific one. Look for duplicates and remove the fallback.
                    var propertyNode = node.Children[j] as TagHelperPropertyIntermediateNode;
                    if (propertyNode != null &&
                        propertyNode.TagHelper.IsBindTagHelper() && 
                        propertyNode.TagHelper.IsFallbackBindTagHelper())
                    {
                        for (var k = 0; k < node.Children.Count; k++)
                        {
                            var duplicate = node.Children[k] as TagHelperPropertyIntermediateNode;
                            if (duplicate != null &&
                                duplicate.TagHelper.IsBindTagHelper() &&
                                duplicate.AttributeName == propertyNode.AttributeName &&
                                !object.ReferenceEquals(propertyNode, duplicate))
                            {
                                // Found a duplicate - remove the 'fallback' in favor of the
                                // more specific tag helper.
                                node.Children.RemoveAt(j);
                                node.TagHelpers.Remove(propertyNode.TagHelper);
                                break;
                            }
                        }
                    }
                }

                for (var j = node.Children.Count - 1; j >= 0; j--)
                {
                    var propertyNode = node.Children[j] as TagHelperPropertyIntermediateNode;
                    if (propertyNode != null &&
                        propertyNode.TagHelper.IsBindTagHelper())
                    {
                        RewriteUsage(node, j, propertyNode);
                    }
                }
            }
        }

        private void RewriteUsage(TagHelperIntermediateNode node, int index, TagHelperPropertyIntermediateNode propertyNode)
        {
            // Bind works similarly to a macro, it always expands to code that the user could have written.
            //
            // For the nodes that are related to the bind-attribute rewrite them to look like a pair of
            // 'normal' HTML attributes similar to the following transformation.
            //
            // Input:   <MyComponent bind-Value="@currentCount" />
            // Output:  <MyComponent Value ="...<get the value>..." ValueChanged ="... <set the value>..." />
            //
            // This means that the expression that appears inside of 'bind' must be an LValue or else
            // there will be errors. In general the errors that come from C# in this case are good enough
            // to understand the problem.
            //
            // The BindMethods calls are required in this case because to give us a good experience. They
            // use overloading to ensure that can get an Action<object> that will convert and set an arbitrary
            // value.
            //
            // We also assume that the element will be treated as a component for now because
            // multiple passes handle 'special' tag helpers. We have another pass that translates
            // a tag helper node back into 'regular' element when it doesn't have an associated component
            if (!TryComputeAttributeNames(
                node,
                propertyNode.AttributeName,
                out var valueAttributeName,
                out var changeHandlerAttributeName,
                out var valueAttribute,
                out var changeHandlerAttribute))
            {
                // Skip anything we can't understand. It's important that we don't crash, that will bring down
                // the build.
                return;
            }

            if (HasComplexChildContent(propertyNode))
            {
                node.Diagnostics.Add(BlazorDiagnosticFactory.Create_UnsupportedComplexContent(
                    propertyNode,
                    propertyNode.AttributeName));
                node.Children.Remove(propertyNode);
                return;
            }

            var originalContent = GetAttributeContent(propertyNode);
            if (string.IsNullOrEmpty(originalContent))
            {
                // This can happen in error cases, the parser will already have flagged this
                // as an error, so ignore it.
                return;
            }
            
            var valueNode = new ComponentAttributeExtensionNode(propertyNode)
            {
                AttributeName = valueAttributeName,
                BoundAttribute = valueAttribute, // Might be null if it doesn't match a component attribute
                PropertyName = valueAttribute?.GetPropertyName(),
                TagHelper = valueAttribute == null ? null : propertyNode.TagHelper,
            };
            node.Children.Insert(index, valueNode);

            // Now rewrite the content of the value node to look like:
            //
            // BindMethods.GetValue(<code>)
            //
            // For now, the way this is done isn't debuggable. But since the expression
            // passed here must be an LValue, it's probably not important.
            valueNode.Children.Clear();
            valueNode.Children.Add(new CSharpExpressionIntermediateNode()
            {
                Children =
                {
                    new IntermediateToken()
                    {
                        Content = $"{BlazorApi.BindMethods.GetValue}({originalContent})",
                        Kind = TokenKind.CSharp
                    },
                },
            });
            
            var changeHandlerNode = new ComponentAttributeExtensionNode(propertyNode)
            {
                AttributeName = changeHandlerAttributeName,
                BoundAttribute = changeHandlerAttribute, // Might be null if it doesn't match a component attribute
                PropertyName = changeHandlerAttribute?.GetPropertyName(),
                TagHelper = changeHandlerAttribute == null ? null : propertyNode.TagHelper,
            };
            node.Children[index + 1] = changeHandlerNode;

            // Now rewrite the content of the change-handler node. There are two cases we care about
            // here. If it's a component attribute, then don't use the 'BindMethods wrapper. We expect
            // component attributes to always 'match' on type.
            //
            // __value => <code> = __value
            //
            // For general DOM attributes, we need to be able to create a delegate that accepts UIEventArgs
            // so we use BindMethods.SetValueHandler
            //
            // BindMethods.SetValueHandler(__value => <code> = __value, <code>)
            //
            // For now, the way this is done isn't debuggable. But since the expression
            // passed here must be an LValue, it's probably not important.
            var content = changeHandlerNode.BoundAttribute == null ?
                $"{BlazorApi.BindMethods.SetValueHandler}(__value => {originalContent} = __value, {originalContent})" :
                $"__value => {originalContent} = __value";

            changeHandlerNode.Children.Clear();
            changeHandlerNode.Children.Add(new CSharpExpressionIntermediateNode()
            {
                Children =
                {
                    new IntermediateToken()
                    {
                        Content = content,
                        Kind = TokenKind.CSharp
                    },
                },
            });
        }

        private bool TryParseBindAttribute(
            string attributeName,
            out string valueAttributeName,
            out string changeHandlerAttributeName)
        {
            valueAttributeName = null;
            changeHandlerAttributeName = null;

            if (!attributeName.StartsWith("bind"))
            {
                return false;
            }

            if (attributeName == "bind")
            {
                return true;
            }

            var segments = attributeName.Split('-');
            for (var i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[0]))
                {
                    return false;
                }
            }

            switch (segments.Length)
            {
                case 2:
                    valueAttributeName = segments[1];
                    return true;

                case 3:
                    changeHandlerAttributeName = segments[2];
                    valueAttributeName = segments[1];
                    return true;

                default:
                    return false;
            }
        }

        // Attempts to compute the attribute names that should be used for an instance of 'bind'.
        private bool TryComputeAttributeNames(
            TagHelperIntermediateNode node,
            string attributeName,
            out string valueAttributeName,
            out string changeHandlerAttributeName,
            out BoundAttributeDescriptor valueAttribute,
            out BoundAttributeDescriptor changeHandlerAttribute)
        {
            valueAttribute = null;
            changeHandlerAttribute = null;
            
            // Even though some of our 'bind' tag helpers specify the attribute names, they
            // should still satisfy one of the valid syntaxes.
            if (!TryParseBindAttribute(attributeName, out valueAttributeName, out changeHandlerAttributeName))
            {
                return false;
            }

            // If we the attibute doesn't specify the names, then ask the tag helper. This handles cases
            // like <input type="text" bind="@Foo" /> where the tag helper is generated to match a specific
            // tag and has metadata that identify the attributes.
            //
            // We expect 1 bind tag helper per-node.
            var bindTagHelper = node.TagHelpers.Single(t => t.IsBindTagHelper());
            valueAttributeName = valueAttributeName ?? bindTagHelper.GetValueAttributeName();
            changeHandlerAttributeName = changeHandlerAttributeName ?? bindTagHelper.GetChangeHandlerAttributeName();

            // We expect 0-1 components per-node.
            var componentTagHelper = node.TagHelpers.FirstOrDefault(t => t.IsComponentTagHelper());
            if (componentTagHelper == null)
            {
                // If it's not a component node then there isn't too much else to figure out.
                return attributeName != null && changeHandlerAttributeName != null;
            }

            // If this is a component, we need an attribute name for the value.
            if (attributeName == null)
            {
                return false;
            }

            // If this is a component, then we can infer '<PropertyName>Changed' as the name
            // of the change handler.
            if (changeHandlerAttributeName == null)
            {
                changeHandlerAttributeName = valueAttributeName + "Changed";
            }

            for (var i = 0; i < componentTagHelper.BoundAttributes.Count; i++)
            {
                var attribute = componentTagHelper.BoundAttributes[i];

                if (string.Equals(valueAttributeName, attribute.Name))
                {
                    valueAttribute = attribute;
                }

                if (string.Equals(changeHandlerAttributeName, attribute.Name))
                {
                    changeHandlerAttribute = attribute;
                }
            }

            return true;
        }

        private static bool HasComplexChildContent(IntermediateNode node)
        {
            if (node.Children.Count == 1 &&
                node.Children[0] is HtmlAttributeIntermediateNode htmlNode &&
                htmlNode.Children.Count > 1)
            {
                // This case can be hit for a 'string' attribute
                return true;
            }
            else if (node.Children.Count == 1 &&
                node.Children[0] is CSharpExpressionIntermediateNode cSharpNode &&
                cSharpNode.Children.Count > 1)
            {
                // This case can be hit when the attribute has an explicit @ inside, which
                // 'escapes' any special sugar we provide for codegen.
                return true;
            }
            else if (node.Children.Count > 1)
            {
                // This is the common case for 'mixed' content
                return true;
            }

            return false;
        }

        private static string GetAttributeContent(TagHelperPropertyIntermediateNode node)
        {
            if (node.Children[0] is HtmlAttributeIntermediateNode htmlNode)
            {
                // This case can be hit for a 'string' attribute
                return ((IntermediateToken)htmlNode.Children.Single()).Content;
            }
            else if (node.Children[0] is CSharpExpressionIntermediateNode cSharpNode)
            {
                // This case can be hit when the attribute has an explicit @ inside, which
                // 'escapes' any special sugar we provide for codegen.
                return ((IntermediateToken)cSharpNode.Children.Single()).Content;
            }
            else
            {
                // This is the common case for 'mixed' content
                return ((IntermediateToken)node.Children.Single()).Content;
            }
        }
    }
}
