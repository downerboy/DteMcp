using System;
using System.Collections;
using System.Collections.Generic;
using DteMcp.Mcp;
using Xunit;

namespace DteMcp.Tests
{
    public class ToolArgumentsTests
    {
        private static ToolArguments With(string key, object value)
        {
            return new ToolArguments(new Dictionary<string, object> { { key, value } });
        }

        [Fact]
        public void NullDictionary_HasNothing()
        {
            Assert.False(new ToolArguments(null).Has("x"));
        }

        [Fact]
        public void Has_IsFalseForNullValues()
        {
            Assert.False(With("x", null).Has("x"));
        }

        [Fact]
        public void RequireString_ThrowsWhenMissingOrEmpty()
        {
            Assert.Throws<ArgumentException>(() => new ToolArguments(null).RequireString("x"));
            Assert.Throws<ArgumentException>(() => With("x", "").RequireString("x"));
        }

        [Fact]
        public void GetInt_ConvertsDecimalAndFallsBack()
        {
            Assert.Equal(7, With("n", 7m).GetInt("n", 0));
            Assert.Equal(3, new ToolArguments(null).GetInt("n", 3));
        }

        [Fact]
        public void RequireInt_ThrowsWhenMissing()
        {
            Assert.Throws<ArgumentException>(() => new ToolArguments(null).RequireInt("n"));
        }

        [Fact]
        public void GetBool_ReadsValueOrFallback()
        {
            Assert.False(With("b", false).GetBool("b", true));
            Assert.True(new ToolArguments(null).GetBool("b", true));
        }

        [Fact]
        public void GetStringList_ReadsArraysAndIgnoresPlainStrings()
        {
            Assert.Equal(new[] { "a", "b" }, With("l", new ArrayList { "a", "b" }).GetStringList("l"));
            Assert.Empty(With("l", "abc").GetStringList("l"));
            Assert.Empty(new ToolArguments(null).GetStringList("l"));
        }
    }
}
