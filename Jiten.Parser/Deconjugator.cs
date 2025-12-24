using System.Collections.Concurrent;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace Jiten.Parser;

public class Deconjugator
{
    private static readonly Lazy<Deconjugator> _instance =
        new Lazy<Deconjugator>(() => new Deconjugator(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static Deconjugator Instance => _instance.Value;

    public List<DeconjugationRule> Rules = new();

    // Cache virtual rules to avoid recreating them
    private readonly Dictionary<DeconjugationRule, DeconjugationVirtualRule[]> _virtualRulesCache = new();
    
    // Object pools for frequently allocated collections
    private static readonly ConcurrentQueue<List<string>> _tagListPool = new();
    private static readonly ConcurrentQueue<List<string>> _processListPool = new();
    private static readonly ConcurrentQueue<HashSet<string>> _seenTextPool = new();

    private static readonly bool UseCache = true;

    private static readonly ConcurrentDictionary<string, HashSet<DeconjugationForm>> DeconjugationCache
        = new(StringComparer.Ordinal);

    public Deconjugator()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new StringArrayConverter() }
        };
        
        var rules = JsonSerializer
            .Deserialize<List<DeconjugationRule>>(
                File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "deconjugator.json")),
                options);
        
        foreach (var rule in rules!)
        {
            Rules.Add(rule);
            // Pre-cache virtual rules
            CacheVirtualRules(rule);
        }
    }

    private void CacheVirtualRules(DeconjugationRule rule)
    {
        if (rule.DecEnd.Length <= 1) return;

        var virtualRules = new DeconjugationVirtualRule[rule.DecEnd.Length];
        for (int i = 0; i < rule.DecEnd.Length; i++)
        {
            virtualRules[i] = new DeconjugationVirtualRule(
                rule.DecEnd.ElementAtOrDefault(i) ?? rule.DecEnd[0],
                rule.ConEnd.ElementAtOrDefault(i) ?? rule.ConEnd[0],
                rule.DecTag?.ElementAtOrDefault(i) ?? rule.DecTag?[0],
                rule.ConTag?.ElementAtOrDefault(i) ?? rule.ConTag?[0],
                rule.Detail
            );
        }
        _virtualRulesCache[rule] = virtualRules;
    }

    // Object pool helpers
    private static List<string> RentTagList()
    {
        if (_tagListPool.TryDequeue(out var list))
        {
            list.Clear();
            return list;
        }
        return new List<string>();
    }

    private static void ReturnTagList(List<string> list)
    {
        if (list.Capacity <= 16) // Don't pool overly large lists
            _tagListPool.Enqueue(list);
    }

    private static List<string> RentProcessList()
    {
        if (_processListPool.TryDequeue(out var list))
        {
            list.Clear();
            return list;
        }
        return new List<string>();
    }

    private static void ReturnProcessList(List<string> list)
    {
        if (list.Capacity <= 16)
            _processListPool.Enqueue(list);
    }

    private static HashSet<string> RentSeenTextSet()
    {
        if (_seenTextPool.TryDequeue(out var set))
        {
            set.Clear();
            return set;
        }
        return new HashSet<string>(StringComparer.Ordinal);
    }

    private static void ReturnSeenTextSet(HashSet<string> set)
    {
        if (set.Count <= 16)
            _seenTextPool.Enqueue(set);
    }

    public HashSet<DeconjugationForm> Deconjugate(string text)
    {
        if (UseCache && DeconjugationCache.TryGetValue(text, out var cached))
        {
            return new HashSet<DeconjugationForm>(cached);
        }

        var processed = new HashSet<DeconjugationForm>(Math.Min(text.Length * 2, 100));

        if (string.IsNullOrEmpty(text))
            return processed;

        var novel = new HashSet<DeconjugationForm>(20);
        var startForm = CreateInitialForm(text);
        novel.Add(startForm);

        // Use arrays for better performance in inner loops
        var ruleArray = Rules.ToArray();
        var ruleCount = ruleArray.Length;

        while (novel.Count > 0)
        {
            var newNovel = new HashSet<DeconjugationForm>(novel.Count * 2);
            
            foreach (var form in novel)
            {
                if (ShouldSkipForm(form)) 
                    continue;

                // Use for loop instead of foreach for better performance
                for (int i = 0; i < ruleCount; i++)
                {
                    var rule = ruleArray[i];
                    var newForms = ApplyRule(form, rule);

                    if (newForms == null) continue;

                    foreach (var f in newForms)
                    {
                        if (!processed.Contains(f) && !novel.Contains(f) && !newNovel.Contains(f))
                            newNovel.Add(f);
                    }
                }
            }

            processed.UnionWith(novel);
            novel = newNovel;
        }

        if (UseCache && text.Length <= 20 && processed.Count < 55 && DeconjugationCache.Count < 250000)
        {
            DeconjugationCache[text] = new HashSet<DeconjugationForm>(processed);
        }

        return processed;
    }

    private DeconjugationForm CreateInitialForm(string text)
    {
        var tags = RentTagList();
        var seenText = RentSeenTextSet();
        var process = RentProcessList();
        
        return new DeconjugationForm(text, text, tags, seenText, process);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HashSet<DeconjugationForm>? ApplyRule(DeconjugationForm form, DeconjugationRule rule)
    {
        return rule.Type switch
        {
            "stdrule" => StdRuleDeconjugate(form, rule),
            "rewriterule" => RewriteRuleDeconjugate(form, rule),
            "onlyfinalrule" => OnlyFinalRuleDeconjugate(form, rule),
            "neverfinalrule" => NeverFinalRuleDeconjugate(form, rule),
            "contextrule" => ContextRuleDeconjugate(form, rule),
            "substitution" => SubstitutionDeconjugate(form, rule),
            _ => null
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldSkipForm(DeconjugationForm form)
    {
        return string.IsNullOrEmpty(form.Text) ||
               form.Text.Length > form.OriginalText.Length + 10 ||
               form.Tags.Count > form.OriginalText.Length + 6;
    }

    private HashSet<DeconjugationForm>? StdRuleDeconjugate(DeconjugationForm form, DeconjugationRule rule)
    {
        if (string.IsNullOrEmpty(rule.Detail) && form.Tags.Count == 0)
            return null;

        if (rule.DecEnd.Length == 1)
        {
            var virtualRule = new DeconjugationVirtualRule(
                rule.DecEnd[0],
                rule.ConEnd[0],
                rule.DecTag?[0],
                rule.ConTag?[0],
                rule.Detail
            );

            if (StdRuleDeconjugateInner(form, virtualRule) is { } hit)
                return new HashSet<DeconjugationForm>(1) { hit };

            return null;
        }

        if (!_virtualRulesCache.TryGetValue(rule, out var cachedVirtualRules))
            return null;

        HashSet<DeconjugationForm>? collection = null;
        
        foreach (var virtualRule in cachedVirtualRules)
        {
            if (StdRuleDeconjugateInner(form, virtualRule) is { } hit)
            {
                collection ??= new HashSet<DeconjugationForm>(cachedVirtualRules.Length);
                collection.Add(hit);
            }
        }

        return collection;
    }

    private DeconjugationForm? StdRuleDeconjugateInner(DeconjugationForm form, DeconjugationVirtualRule rule)
    {
        if (!form.Text.EndsWith(rule.ConEnd, StringComparison.Ordinal))
            return null;

        if (form.Tags.Count > 0 && form.Tags[^1] != rule.ConTag)
            return null;

        var prefixLength = form.Text.Length - rule.ConEnd.Length;
        
        // Use stackalloc for small strings to avoid heap allocation
        Span<char> buffer = stackalloc char[prefixLength + rule.DecEnd.Length];
        form.Text.AsSpan(0, prefixLength).CopyTo(buffer);
        rule.DecEnd.AsSpan().CopyTo(buffer[prefixLength..]);
        var newText = new string(buffer);

        if (newText.Equals(form.OriginalText, StringComparison.Ordinal))
            return null;

        return CreateNewForm(form, newText, rule.ConTag, rule.DecTag, rule.Detail);
    }

    private DeconjugationForm CreateNewForm(DeconjugationForm form, string newText, string? conTag, string? decTag, string detail)
    {
        var newTags = RentTagList();
        newTags.AddRange(form.Tags);
        
        var newSeenText = RentSeenTextSet();
        foreach (var item in form.SeenText)
            newSeenText.Add(item);
        
        var newProcess = RentProcessList();
        newProcess.AddRange(form.Process);

        var newForm = new DeconjugationForm(newText, form.OriginalText, newTags, newSeenText, newProcess);

        newForm.Process.Add(detail);

        if (newForm.Tags.Count == 0 && conTag != null)
            newForm.Tags.Add(conTag);

        if (decTag != null)
            newForm.Tags.Add(decTag);

        if (newForm.SeenText.Count == 0)
            newForm.SeenText.Add(form.Text);

        newForm.SeenText.Add(newText);

        return newForm;
    }

    private HashSet<DeconjugationForm>? SubstitutionDeconjugate(DeconjugationForm form, DeconjugationRule rule)
    {
        if (form.Process.Count != 0 || string.IsNullOrEmpty(form.Text))
            return null;

        if (rule.DecEnd.Length == 1)
        {
            if (SubstitutionInnerOptimized(form, rule.ConEnd[0], rule.DecEnd[0], rule.Detail) is { } hit)
                return new HashSet<DeconjugationForm>(1) { hit };
            return null;
        }

        HashSet<DeconjugationForm>? collection = null;
        
        for (int i = 0; i < rule.DecEnd.Length; i++)
        {
            var decEnd = rule.DecEnd.ElementAtOrDefault(i) ?? rule.DecEnd[0];
            var conEnd = rule.ConEnd.ElementAtOrDefault(i) ?? rule.ConEnd[0];
            
            if (SubstitutionInnerOptimized(form, conEnd, decEnd, rule.Detail) is { } ret)
            {
                collection ??= new HashSet<DeconjugationForm>(rule.DecEnd.Length);
                collection.Add(ret);
            }
        }

        return collection;
    }

    private DeconjugationForm? SubstitutionInnerOptimized(DeconjugationForm form, string conEnd, string decEnd, string detail)
    {
        if (!form.Text.Contains(conEnd, StringComparison.Ordinal))
            return null;

        var newText = form.Text.Replace(conEnd, decEnd, StringComparison.Ordinal);
        return CreateSubstitutionForm(form, newText, detail);
    }

    private DeconjugationForm CreateSubstitutionForm(DeconjugationForm form, string newText, string detail)
    {
        var newSeenText = RentSeenTextSet();
        foreach (var item in form.SeenText)
            newSeenText.Add(item);
        
        var newProcess = RentProcessList();
        newProcess.AddRange(form.Process);
        
        var newTags = RentTagList();
        newTags.AddRange(form.Tags);

        var newForm = new DeconjugationForm(newText, form.OriginalText, newTags, newSeenText, newProcess);

        newForm.Process.Add(detail);

        if (newForm.SeenText.Count == 0)
            newForm.SeenText.Add(form.Text);
        newForm.SeenText.Add(newText);

        return newForm;
    }

    private HashSet<DeconjugationForm>? RewriteRuleDeconjugate(DeconjugationForm form, DeconjugationRule rule)
    {
        return form.Text.Equals(rule.ConEnd[0], StringComparison.Ordinal) ? StdRuleDeconjugate(form, rule) : null;
    }

    private HashSet<DeconjugationForm>? OnlyFinalRuleDeconjugate(DeconjugationForm form, DeconjugationRule rule)
    {
        return form.Tags.Count == 0 ? StdRuleDeconjugate(form, rule) : null;
    }

    private HashSet<DeconjugationForm>? NeverFinalRuleDeconjugate(DeconjugationForm form, DeconjugationRule rule)
    {
        return form.Tags.Count != 0 ? StdRuleDeconjugate(form, rule) : null;
    }

    private HashSet<DeconjugationForm>? ContextRuleDeconjugate(DeconjugationForm form, DeconjugationRule rule)
    {
        if (rule.ContextRule == "v1inftrap" && !V1InfTrapCheck(form))
            return null;

        if (rule.ContextRule == "saspecial" && !SaSpecialCheck(form, rule))
            return null;

        return StdRuleDeconjugate(form, rule);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool V1InfTrapCheck(DeconjugationForm form)
    {
        return form.Tags is not ["stem-ren"];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SaSpecialCheck(DeconjugationForm form, DeconjugationRule rule)
    {
        if (form.Text.Length == 0) return false;

        var conEnd = rule.ConEnd[0];
        if (!form.Text.EndsWith(conEnd, StringComparison.Ordinal)) return false;

        var prefixLength = form.Text.Length - conEnd.Length;
        return prefixLength <= 0 || !form.Text.AsSpan(prefixLength - 1, 1).SequenceEqual("さ".AsSpan());
    }
}