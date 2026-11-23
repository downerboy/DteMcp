using System;
using System.Linq;
using DteMcp.Tools;
using Xunit;

namespace DteMcp.Tests
{
    public class ObjectPathTests
    {
        [Fact]
        public void Parse_SplitsMembersOnDots()
        {
            var segments = ObjectPath.Parse("Solution.Projects.Count");

            Assert.Equal(new[] { "Solution", "Projects", "Count" }, segments.Select(s => s.Name));
            Assert.All(segments, s => Assert.False(s.HasArguments));
        }

        [Fact]
        public void Parse_ReadsLiteralArguments()
        {
            var segment = ObjectPath.Parse("Add(\"a\", 2, 3000000000, 1.5, true, FALSE, null)").Single();

            Assert.True(segment.HasArguments);
            Assert.Equal(new object[] { "a", 2, 3000000000L, 1.5, true, false, null }, segment.Arguments);
        }

        [Fact]
        public void Parse_KeepsDotsAndCommasInsideStrings()
        {
            var segments = ObjectPath.Parse("Documents.Item(\"C:\\\\my.dir\\\\a,b.cs\").Save()");

            Assert.Equal(new[] { "Documents", "Item", "Save" }, segments.Select(s => s.Name));
            Assert.Equal("C:\\my.dir\\a,b.cs", segments[1].Arguments.Single());
        }

        [Fact]
        public void Parse_UnescapesQuotesAndNewlines()
        {
            var segment = ObjectPath.Parse("Say(\"a\\\"b\\nc\")").Single();

            Assert.Equal("a\"b\nc", segment.Arguments.Single());
        }

        [Fact]
        public void Parse_EmptyParenthesesMeanACallWithoutArguments()
        {
            var segment = ObjectPath.Parse("Save()").Single();

            Assert.True(segment.HasArguments);
            Assert.Empty(segment.Arguments);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("A..B")]
        [InlineData("A.1B")]
        [InlineData("Item(1")]
        [InlineData("Item(1))")]
        [InlineData("Item(\"x)")]
        [InlineData("Item(1)(2)")]
        [InlineData("Item(foo)")]
        [InlineData("Item(Other(1))")]
        public void Parse_RejectsMalformedPaths(string path)
        {
            Assert.Throws<FormatException>(() => ObjectPath.Parse(path));
        }
    }
}
