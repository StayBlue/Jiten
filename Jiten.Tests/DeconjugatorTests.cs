using FluentAssertions;
using Jiten.Parser;

namespace Jiten.Tests;

/// <summary>
/// These tests are not functional for now
/// </summary>
public class DeconjugatorTests
{
    [Theory]
    [InlineData("終わってしまった", new[] { "終わる" })]
    [InlineData("わからない", new[] { "わかる" })]
    [InlineData("みて", new[] { "みる" })]
    [InlineData("作る", new[] { "作る" })]
    [InlineData("なかった", new[] { "ない" })]
    public Task DeconjugationTest(string text, string[] expectedResult)
    {
         Deconjugate(text).Select(r => r.Text).Should().Equal(expectedResult);
         return Task.CompletedTask;
    }
    
    private List<DeconjugationForm> Deconjugate(string text)
    {
        var deconjugator = new Deconjugator();
        return deconjugator.Deconjugate(text);
    }
}