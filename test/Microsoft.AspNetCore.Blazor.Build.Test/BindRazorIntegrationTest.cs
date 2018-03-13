// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Blazor.Test.Helpers;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Microsoft.AspNetCore.Blazor.Build.Test
{
    public class BindRazorIntegrationTest : RazorIntegrationTestBase
    {
        internal override bool UseTwoPhaseCompilation => true;

        [Fact]
        public void Render_BindToComponent_SpecifiesValue_WithMatchingProperties()
        {
            // Arrange
            AdditionalSyntaxTrees.Add(CSharpSyntaxTree.ParseText(@"
using System;
using Microsoft.AspNetCore.Blazor.Components;

namespace Test
{
    public class MyComponent : BlazorComponent
    {
        public int Value { get; set; }

        public Action<int> ValueChanged { get; set; }
    }
}"));

            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<MyComponent bind-Value=""ParentValue"" />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Component(frame, "Test.MyComponent", 3, 0),
                frame => AssertFrame.Attribute(frame, "Value", 42, 1),
                frame => AssertFrame.Attribute(frame, "ValueChanged", typeof(Action<int>), 2),
                frame => AssertFrame.Whitespace(frame, 3));
        }

        [Fact]
        public void Render_BindToComponent_SpecifiesValue_WithoutMatchingProperties()
        {
            // Arrange
            AdditionalSyntaxTrees.Add(CSharpSyntaxTree.ParseText(@"
using System;
using Microsoft.AspNetCore.Blazor.Components;

namespace Test
{
    public class MyComponent : BlazorComponent, IComponent
    {
        void IComponent.SetParameters(ParameterCollection parameters)
        {
        }
    }
}"));

            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<MyComponent bind-Value=""ParentValue"" />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Component(frame, "Test.MyComponent", 3, 0),
                frame => AssertFrame.Attribute(frame, "Value", 42, 1),
                frame => AssertFrame.Attribute(frame, "ValueChanged", typeof(UIEventHandler), 2),
                frame => AssertFrame.Whitespace(frame, 3));
        }

        [Fact]
        public void Render_BindToComponent_SpecifiesValueAndChangeEvent_WithMatchingProperties()
        {
            // Arrange
            AdditionalSyntaxTrees.Add(CSharpSyntaxTree.ParseText(@"
using System;
using Microsoft.AspNetCore.Blazor.Components;

namespace Test
{
    public class MyComponent : BlazorComponent
    {
        public int Value { get; set; }

        public Action<int> OnChanged { get; set; }
    }
}"));

            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<MyComponent bind-Value-OnChanged=""ParentValue"" />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Component(frame, "Test.MyComponent", 3, 0),
                frame => AssertFrame.Attribute(frame, "Value", 42, 1),
                frame => AssertFrame.Attribute(frame, "OnChanged", typeof(Action<int>), 2),
                frame => AssertFrame.Whitespace(frame, 3));
        }

        [Fact]
        public void Render_BindToComponent_SpecifiesValueAndChangeEvent_WithoutMatchingProperties()
        {
            // Arrange
            AdditionalSyntaxTrees.Add(CSharpSyntaxTree.ParseText(@"
using System;
using Microsoft.AspNetCore.Blazor.Components;

namespace Test
{
    public class MyComponent : BlazorComponent, IComponent
    {
        void IComponent.SetParameters(ParameterCollection parameters)
        {
        }
    }
}"));

            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<MyComponent bind-Value-OnChanged=""ParentValue"" />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Component(frame, "Test.MyComponent", 3, 0),
                frame => AssertFrame.Attribute(frame, "Value", 42, 1),
                frame => AssertFrame.Attribute(frame, "OnChanged", typeof(UIEventHandler), 2),
                frame => AssertFrame.Whitespace(frame, 3));
        }

        [Fact]
        public void Render_BindToInputText_Simple()
        {
            // Arrange
            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<input type=""text"" bind=""@ParentValue"" />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Element(frame, "input", 4, 0),
                frame => AssertFrame.Attribute(frame, "type", "text", 1),
                frame => AssertFrame.Attribute(frame, "value", "42", 2),
                frame => AssertFrame.Attribute(frame, "changed", typeof(UIEventHandler), 3),
                frame => AssertFrame.Whitespace(frame, 4));
        }

        [Fact]
        public void Render_BindToInputCheckbox_Simple()
        {
            // Arrange
            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<input type=""checkbox"" bind=""@Enabled"" />
@functions {
    public bool Enabled { get; set; }
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Element(frame, "input", 4, 0),
                frame => AssertFrame.Attribute(frame, "type", "checkbox", 1),
                frame => AssertFrame.Attribute(frame, "checked", "False", 2),
                frame => AssertFrame.Attribute(frame, "changed", typeof(UIEventHandler), 3),
                frame => AssertFrame.Whitespace(frame, 4));
        }

        [Fact]
        public void Render_BindToInputText_SpecifiesValue()
        {
            // Arrange
            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<input type=""text"" bind-value=""@ParentValue"" />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Element(frame, "input", 4, 0),
                frame => AssertFrame.Attribute(frame, "type", "text", 1),
                frame => AssertFrame.Attribute(frame, "value", "42", 2),
                frame => AssertFrame.Attribute(frame, "changed", typeof(UIEventHandler), 3),
                frame => AssertFrame.Whitespace(frame, 4));
        }

        [Fact]
        public void Render_BindToInputCheckbox_SpecifiesValue()
        {
            // Arrange
            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<input type=""checkbox"" bind-checked=""@Enabled"" />
@functions {
    public bool Enabled { get; set; }
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Element(frame, "input", 4, 0),
                frame => AssertFrame.Attribute(frame, "type", "checkbox", 1),
                frame => AssertFrame.Attribute(frame, "checked", "False", 2),
                frame => AssertFrame.Attribute(frame, "changed", typeof(UIEventHandler), 3),
                frame => AssertFrame.Whitespace(frame, 4));
        }

        [Fact]
        public void Render_BindToElement_SpecifiesValueAndChangeEvent()
        {
            // Arrange
            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<input type=""text"" bind-value-changed=""@ParentValue"" />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Element(frame, "input", 4, 0),
                frame => AssertFrame.Attribute(frame, "type", "text", 1),
                frame => AssertFrame.Attribute(frame, "value", "42", 2),
                frame => AssertFrame.Attribute(frame, "changed", typeof(UIEventHandler), 3),
                frame => AssertFrame.Whitespace(frame, 4));
        }

        [Fact] // Additional coverage of OrphanTagHelperLoweringPass
        public void Render_BindToElement_SpecifiesValueAndChangeEvent_WithCSharpAttribute()
        {
            // Arrange
            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<input type=""@(""text"")"" bind-value-changed=""@ParentValue"" visible />
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Element(frame, "input", 5, 0),
                frame => AssertFrame.Attribute(frame, "visible", 1), // This gets reordered in the node writer
                frame => AssertFrame.Attribute(frame, "type", "text", 2),
                frame => AssertFrame.Attribute(frame, "value", "42", 3),
                frame => AssertFrame.Attribute(frame, "changed", typeof(UIEventHandler), 4),
                frame => AssertFrame.Whitespace(frame, 5));
        }

        [Fact] // Additional coverage of OrphanTagHelperLoweringPass
        public void Render_BindToElement_SpecifiesValueAndChangeEvent_BodyContent()
        {
            // Arrange
            var component = CompileToComponent(@"
@addTagHelper *, TestAssembly
<div bind-value-changed=""@ParentValue"">
  <span>@(42.ToString())</span>
</div>
@functions {
    public int ParentValue { get; set; } = 42;
}");

            // Act
            var frames = GetRenderTree(component);

            // Assert
            Assert.Collection(
                frames,
                frame => AssertFrame.Element(frame, "div", 7, 0),
                frame => AssertFrame.Attribute(frame, "value", "42", 1),
                frame => AssertFrame.Attribute(frame, "changed", typeof(UIEventHandler), 2),
                frame => AssertFrame.Whitespace(frame, 3),
                frame => AssertFrame.Element(frame, "span", 2, 4),
                frame => AssertFrame.Text(frame, "42", 5),
                frame => AssertFrame.Whitespace(frame, 6),
                frame => AssertFrame.Whitespace(frame, 7));
        }
    }
}
