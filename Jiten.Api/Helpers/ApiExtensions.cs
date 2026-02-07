using Jiten.Api.Dtos;
using Jiten.Core.Data.JMDict;

namespace Jiten.Api.Helpers;

public static class ApiExtensions
{
    public static List<DefinitionDto> ToDefinitionDtos(this List<JmDictDefinition> definitions)
    {
        int i = 1;
        List<DefinitionDto> definitionDtos = new();
        foreach (var definition in definitions.OrderBy(d => d.DefinitionId))
        {
            if (definition.EnglishMeanings.Count == 0)
                continue;

            definitionDtos.Add(new DefinitionDto
                               {
                                   Index = i++,
                                   Meanings = definition.EnglishMeanings,
                                   PartsOfSpeech = definition.PartsOfSpeech.ToHumanReadablePartsOfSpeech(),
                                   Pos = definition.Pos.Count > 0 ? definition.Pos : null,
                                   Misc = definition.Misc.Count > 0 ? definition.Misc : null,
                                   Field = definition.Field.Count > 0 ? definition.Field.ToHumanReadablePartsOfSpeech() : null,
                                   Dial = definition.Dial.Count > 0 ? definition.Dial.ToHumanReadablePartsOfSpeech() : null,
                                   RestrictedToReadingIndices = definition.RestrictedToReadingIndices
                               });
        }

        return definitionDtos;
    }
}