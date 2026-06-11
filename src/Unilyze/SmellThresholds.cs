using System.Text;

namespace Unilyze;

/// <summary>
/// Single source of truth for code-smell detection thresholds.
/// Consumed by detectors, CLI metrics output, SARIF rule metadata, and docs drift tests.
/// </summary>
public static class SmellThresholds
{
    public const int GodClassLinesWarning = 500;
    public const int GodClassMethodsWarning = 20;
    public const int GodClassLinesCritical = 1000;

    public const int LongMethodLinesWarning = 80;
    public const int LongMethodCogCcWarning = 25;
    public const int LongMethodLinesCritical = 150;
    public const int LongMethodCogCcCritical = 40;

    /// <summary>Maximum parameter count before ExcessiveParameters (smell when count exceeds this).</summary>
    public const int ExcessiveParametersMax = 5;

    public const int HighComplexityCycCcWarning = 15;
    public const int HighComplexityCogCcWarning = 15;

    public const int DeepNestingDepthWarning = 4;
    public const int DeepNestingDepthCritical = 6;

    public const double LowCohesionLcomWarning = 0.8;

    public const int HighCouplingCboWarning = 15;
    public const int HighCouplingCboCritical = 25;

    public const double LowMaintainabilityMiWarning = 60.0;

    public const int DeepInheritanceDitWarning = 5;

    public static string FormatMetricsCliHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("    CodeSmell detection thresholds:");
        AppendMetricsLine(sb, "GodClass",
            $"lines >= {GodClassLinesWarning} OR methods >= {GodClassMethodsWarning}",
            $"lines >= {GodClassLinesCritical}");
        AppendMetricsLine(sb, "LongMethod",
            $"lines >= {LongMethodLinesWarning} OR CogCC >= {LongMethodCogCcWarning}",
            $"lines >= {LongMethodLinesCritical} OR CogCC >= {LongMethodCogCcCritical}");
        AppendMetricsLine(sb, "HighComplexity",
            $"CycCC >= {HighComplexityCycCcWarning} OR CogCC >= {HighComplexityCogCcWarning}");
        AppendMetricsLine(sb, "DeepNesting",
            $"depth >= {DeepNestingDepthWarning}",
            $"depth >= {DeepNestingDepthCritical}");
        sb.AppendLine($"      ExcessiveParameters  params > {ExcessiveParametersMax}");
        AppendMetricsLine(sb, "LowCohesion", $"LCOM >= {LowCohesionLcomWarning:0.0}");
        AppendMetricsLine(sb, "HighCoupling",
            $"CBO >= {HighCouplingCboWarning}",
            $"CBO >= {HighCouplingCboCritical}");
        AppendMetricsLine(sb, "DeepInheritance", $"DIT >= {DeepInheritanceDitWarning}");
        AppendMetricsLine(sb, "LowMaintainability", $"MI < {LowMaintainabilityMiWarning:0}");
        sb.AppendLine("      CyclicDependency     type participates in a dependency cycle");
        return sb.ToString().TrimEnd();
    }

    public static string RenderDocsThresholdTable()
    {
        var sb = new StringBuilder();
        sb.AppendLine("| スメル | 判定条件 (Warning) | 判定条件 (Critical) |");
        sb.AppendLine("|--------|-------------------|-------------------|");
        AppendDocsRow(sb, "GodClass",
            $"行数 >= {GodClassLinesWarning} or メソッド数 >= {GodClassMethodsWarning}",
            $"行数 >= {GodClassLinesCritical}");
        AppendDocsRow(sb, "LongMethod",
            $"行数 >= {LongMethodLinesWarning} or CogCC >= {LongMethodCogCcWarning}",
            $"行数 >= {LongMethodLinesCritical} or CogCC >= {LongMethodCogCcCritical}");
        AppendDocsRow(sb, "ExcessiveParameters", $"パラメータ数 > {ExcessiveParametersMax}", "—");
        AppendDocsRow(sb, "HighComplexity",
            $"CycCC >= {HighComplexityCycCcWarning} or CogCC >= {HighComplexityCogCcWarning}", "—");
        AppendDocsRow(sb, "DeepNesting",
            $"ネスト深度 >= {DeepNestingDepthWarning}",
            $"ネスト深度 >= {DeepNestingDepthCritical}");
        AppendDocsRow(sb, "LowCohesion", $"LCOM >= {LowCohesionLcomWarning:0.0}", "—");
        AppendDocsRow(sb, "HighCoupling",
            $"CBO >= {HighCouplingCboWarning}",
            $"CBO >= {HighCouplingCboCritical}");
        AppendDocsRow(sb, "LowMaintainability", $"MI < {LowMaintainabilityMiWarning:0}", "—");
        AppendDocsRow(sb, "DeepInheritance", $"DIT >= {DeepInheritanceDitWarning}", "—");
        AppendDocsRow(sb, "CatchAllException",
            "`catch (Exception)` without rethrow (excluding `when` filtered catches)", "—");
        AppendDocsRow(sb, "AsyncVoidMethod",
            "`async void` method (excluding Unity message methods and event handlers)", "—");
        AppendDocsRow(sb, "BlockingTaskWait",
            "`.Result` / `.Wait()` / `.GetAwaiter().GetResult()` on Task/ValueTask/UniTask", "—");
        return sb.ToString().TrimEnd();
    }

    public static string? GetSarifFullDescription(CodeSmellKind kind)
        => GetSarifFullDescription(kind, EffectiveSmellThresholds.Default);

    public static string? GetSarifFullDescription(CodeSmellKind kind, EffectiveSmellThresholds thresholds) => kind switch
    {
        CodeSmellKind.GodClass =>
            $"Type exceeds size thresholds: lines >= {thresholds.GodClassLinesWarning} or methods >= {thresholds.GodClassMethodsWarning} (Critical: lines >= {thresholds.GodClassLinesCritical}).",
        CodeSmellKind.LongMethod =>
            $"Method exceeds length thresholds: lines >= {thresholds.LongMethodLinesWarning} or CogCC >= {thresholds.LongMethodCogCcWarning} (Critical: lines >= {thresholds.LongMethodLinesCritical} or CogCC >= {thresholds.LongMethodCogCcCritical}).",
        CodeSmellKind.ExcessiveParameters =>
            $"Method has more than {thresholds.ExcessiveParametersMax} parameters.",
        CodeSmellKind.HighComplexity =>
            $"Method complexity exceeds thresholds: CycCC >= {thresholds.HighComplexityCycCcWarning} or CogCC >= {thresholds.HighComplexityCogCcWarning}.",
        CodeSmellKind.DeepNesting =>
            $"Method nesting depth >= {thresholds.DeepNestingDepthWarning} (Critical: >= {thresholds.DeepNestingDepthCritical}).",
        CodeSmellKind.LowCohesion =>
            $"Type LCOM >= {thresholds.LowCohesionLcomWarning:0.0}.",
        CodeSmellKind.HighCoupling =>
            $"Type CBO >= {thresholds.HighCouplingCboWarning} (Critical: >= {thresholds.HighCouplingCboCritical}).",
        CodeSmellKind.LowMaintainability =>
            $"Method MI < {thresholds.LowMaintainabilityMiWarning:0}.",
        CodeSmellKind.CyclicDependency =>
            "Type participates in a dependency cycle.",
        CodeSmellKind.DeepInheritance =>
            $"Type DIT >= {thresholds.DeepInheritanceDitWarning}.",
        CodeSmellKind.BoxingAllocation =>
            "Value type boxed to reference type (object, interface, virtual call).",
        CodeSmellKind.ClosureCapture =>
            "Lambda or anonymous method captures outer scope variable (heap allocation).",
        CodeSmellKind.ParamsArrayAllocation =>
            "Implicit array allocation for params parameter.",
        CodeSmellKind.CatchAllException =>
            "catch (Exception) without rethrow or when filter.",
        CodeSmellKind.MissingInnerException =>
            "throw new X() in catch block without inner exception.",
        CodeSmellKind.ThrowingSystemException =>
            "throw new Exception() directly (use a specific exception type).",
        CodeSmellKind.AsyncVoidMethod =>
            "async void method (excluding Unity message methods and event handlers). Exceptions are not observable by callers; in Unity they surface only in the log (silent failure).",
        CodeSmellKind.BlockingTaskWait =>
            ".Result / .Wait() / .GetAwaiter().GetResult() blocking wait on Task/ValueTask/UniTask (can deadlock or stall the frame on the main thread).",
        CodeSmellKind.WeakTemporization =>
            "Incremental transform mutation in Update/LateUpdate without Time.deltaTime scaling (frame-rate dependent).",
        CodeSmellKind.ExpensiveUnityApiInHotPath =>
            "Expensive Unity API call (GetComponent, Find, Camera.main, etc.) inside MonoBehaviour per-frame methods (Update, FixedUpdate, LateUpdate, OnGUI, coroutines). Cache references; Camera.main is cached on Unity 2020.2+.",
        CodeSmellKind.LinqInHotPath =>
            "LINQ query or operator inside MonoBehaviour per-frame methods (Update, FixedUpdate, LateUpdate, OnGUI, coroutines). LINQ allocates enumerators and often intermediate collections.",
        CodeSmellKind.CollectionAllocationInHotPath =>
            "Collection or array allocation inside MonoBehaviour per-frame methods (Update, FixedUpdate, LateUpdate, OnGUI, coroutines). Reuse buffers or pre-allocate outside hot paths.",
        CodeSmellKind.StringConcatenationInHotPath =>
            "String concatenation, interpolation, or string.Format/Join inside MonoBehaviour per-frame methods (Update, FixedUpdate, LateUpdate, OnGUI, coroutines). Prefer StringBuilder or cached strings.",
        _ => null,
    };

    public const string SarifHelpUri =
        "https://github.com/bigdra50/unilyze/blob/main/docs/metrics.md#code-smell";

    public static string? GetSarifHelpText(CodeSmellKind kind)
        => GetSarifHelpText(kind, EffectiveSmellThresholds.Default);

    public static string? GetSarifHelpText(CodeSmellKind kind, EffectiveSmellThresholds thresholds) => kind switch
    {
        CodeSmellKind.GodClass =>
            $"Split the type into smaller, focused classes (threshold: lines >= {thresholds.GodClassLinesWarning} or methods >= {thresholds.GodClassMethodsWarning}).",
        CodeSmellKind.LongMethod =>
            $"Extract helper methods or decompose control flow (threshold: lines >= {thresholds.LongMethodLinesWarning} or CogCC >= {thresholds.LongMethodCogCcWarning}).",
        CodeSmellKind.ExcessiveParameters =>
            $"Introduce a parameter object or split responsibilities (threshold: params > {thresholds.ExcessiveParametersMax}).",
        CodeSmellKind.HighComplexity =>
            $"Reduce branching and extract logic (threshold: CycCC >= {thresholds.HighComplexityCycCcWarning} or CogCC >= {thresholds.HighComplexityCogCcWarning}).",
        CodeSmellKind.DeepNesting =>
            $"Use guard clauses or extract nested blocks (threshold: depth >= {thresholds.DeepNestingDepthWarning}).",
        CodeSmellKind.LowCohesion =>
            $"Group related methods or split the type (threshold: LCOM >= {thresholds.LowCohesionLcomWarning:0.0}).",
        CodeSmellKind.HighCoupling =>
            $"Reduce direct dependencies via interfaces or facades (threshold: CBO >= {thresholds.HighCouplingCboWarning}).",
        CodeSmellKind.LowMaintainability =>
            $"Simplify the method to raise Maintainability Index (threshold: MI < {thresholds.LowMaintainabilityMiWarning:0}).",
        CodeSmellKind.CyclicDependency =>
            "Break the dependency cycle with interfaces, events, or restructuring.",
        CodeSmellKind.DeepInheritance =>
            $"Prefer composition over deep hierarchies (threshold: DIT >= {thresholds.DeepInheritanceDitWarning}).",
        CodeSmellKind.BoxingAllocation =>
            "Avoid boxing value types; use generics or concrete types instead.",
        CodeSmellKind.ClosureCapture =>
            "Reduce captured variables or hoist allocations outside hot paths.",
        CodeSmellKind.ParamsArrayAllocation =>
            "Replace params with Span<T>, ReadOnlySpan<T>, or explicit overloads.",
        CodeSmellKind.CatchAllException =>
            "Catch specific exception types or rethrow with context.",
        CodeSmellKind.MissingInnerException =>
            "Pass the caught exception as the inner exception when rethrowing.",
        CodeSmellKind.ThrowingSystemException =>
            "Throw a domain-specific exception type instead of System.Exception.",
        CodeSmellKind.AsyncVoidMethod =>
            "Return Task or Task<T> so callers can observe failures.",
        CodeSmellKind.BlockingTaskWait =>
            "Await asynchronously instead of blocking on .Result or .Wait().",
        CodeSmellKind.WeakTemporization =>
            "Scale transform mutations by Time.deltaTime for frame-rate independence.",
        CodeSmellKind.ExpensiveUnityApiInHotPath =>
            "Cache Unity API references outside Update/FixedUpdate/LateUpdate.",
        CodeSmellKind.LinqInHotPath =>
            "Replace LINQ with non-allocating loops in per-frame methods.",
        CodeSmellKind.CollectionAllocationInHotPath =>
            "Reuse buffers or pre-allocate collections outside hot paths.",
        CodeSmellKind.StringConcatenationInHotPath =>
            "Use StringBuilder or cached strings instead of concatenation in hot paths.",
        _ => null,
    };

    public static string? GetSarifHelpMarkdown(CodeSmellKind kind)
        => GetSarifHelpMarkdown(kind, EffectiveSmellThresholds.Default);

    public static string? GetSarifHelpMarkdown(CodeSmellKind kind, EffectiveSmellThresholds thresholds) => kind switch
    {
        CodeSmellKind.GodClass =>
            $"God classes accumulate unrelated responsibilities and resist change.\n\n" +
            $"**Threshold:** lines >= {thresholds.GodClassLinesWarning} or methods >= {thresholds.GodClassMethodsWarning} (Critical: lines >= {thresholds.GodClassLinesCritical}).\n\n" +
            "**How to fix:** extract cohesive method groups into separate types; tune thresholds in `.unilyze.json` if justified.",
        CodeSmellKind.LongMethod =>
            $"Long methods are harder to test, review, and reuse.\n\n" +
            $"**Threshold:** lines >= {thresholds.LongMethodLinesWarning} or CogCC >= {thresholds.LongMethodCogCcWarning} (Critical: lines >= {thresholds.LongMethodLinesCritical} or CogCC >= {thresholds.LongMethodCogCcCritical}).\n\n" +
            "**How to fix:** extract helpers, apply guard clauses, or split by responsibility.",
        CodeSmellKind.ExcessiveParameters =>
            $"Many parameters increase coupling and call-site errors.\n\n" +
            $"**Threshold:** parameter count > {thresholds.ExcessiveParametersMax}.\n\n" +
            "**How to fix:** introduce a parameter object, builder, or split the method.",
        CodeSmellKind.HighComplexity =>
            $"High complexity raises defect risk and review cost.\n\n" +
            $"**Threshold:** CycCC >= {thresholds.HighComplexityCycCcWarning} or CogCC >= {thresholds.HighComplexityCogCcWarning}.\n\n" +
            "**How to fix:** decompose conditionals, extract methods, or simplify control flow.",
        CodeSmellKind.DeepNesting =>
            $"Deep nesting obscures intent and complicates testing.\n\n" +
            $"**Threshold:** depth >= {thresholds.DeepNestingDepthWarning} (Critical: >= {thresholds.DeepNestingDepthCritical}).\n\n" +
            "**How to fix:** use early returns, extract nested blocks, or flatten conditionals.",
        CodeSmellKind.LowCohesion =>
            $"Low cohesion suggests the type mixes unrelated behavior.\n\n" +
            $"**Threshold:** LCOM >= {thresholds.LowCohesionLcomWarning:0.0}.\n\n" +
            "**How to fix:** split unrelated methods into separate types.",
        CodeSmellKind.HighCoupling =>
            $"High coupling makes types fragile to upstream changes.\n\n" +
            $"**Threshold:** CBO >= {thresholds.HighCouplingCboWarning} (Critical: >= {thresholds.HighCouplingCboCritical}).\n\n" +
            "**How to fix:** introduce interfaces, facades, or reduce direct references.",
        CodeSmellKind.LowMaintainability =>
            $"Low Maintainability Index signals hard-to-change code.\n\n" +
            $"**Threshold:** MI < {thresholds.LowMaintainabilityMiWarning:0}.\n\n" +
            "**How to fix:** shorten the method, reduce complexity, or improve naming.",
        CodeSmellKind.CyclicDependency =>
            "Cyclic dependencies prevent clean layering and complicate testing.\n\n" +
            "**How to fix:** break cycles with interfaces, events, or dependency inversion.",
        CodeSmellKind.DeepInheritance =>
            $"Deep inheritance hierarchies obscure behavior and encourage fragile overrides.\n\n" +
            $"**Threshold:** DIT >= {thresholds.DeepInheritanceDitWarning}.\n\n" +
            "**How to fix:** prefer composition; flatten or replace deep chains.",
        CodeSmellKind.BoxingAllocation =>
            "Boxing allocates on the heap and can pressure the GC in hot paths.\n\n" +
            "**How to fix:** use generics, concrete types, or avoid implicit boxing to `object`/interfaces.",
        CodeSmellKind.ClosureCapture =>
            "Captured variables allocate display classes on the heap.\n\n" +
            "**How to fix:** hoist captures, use static lambdas where possible, or reduce scope.",
        CodeSmellKind.ParamsArrayAllocation =>
            "Implicit `params` arrays allocate on every call.\n\n" +
            "**How to fix:** use `Span<T>`, `ReadOnlySpan<T>`, or explicit overloads.",
        CodeSmellKind.CatchAllException =>
            "Broad catches hide failures and complicate recovery.\n\n" +
            "**How to fix:** catch specific types, add `when` filters, or rethrow with context.",
        CodeSmellKind.MissingInnerException =>
            "Rethrowing without an inner exception loses diagnostic context.\n\n" +
            "**How to fix:** pass the caught exception to the new exception constructor.",
        CodeSmellKind.ThrowingSystemException =>
            "Throwing `System.Exception` forces callers to catch everything.\n\n" +
            "**How to fix:** define and throw a domain-specific exception type.",
        CodeSmellKind.AsyncVoidMethod =>
            "`async void` exceptions are not observable by callers; in Unity they may fail silently.\n\n" +
            "**How to fix:** return `Task`/`Task<T>`; reserve `async void` for event handlers only.",
        CodeSmellKind.BlockingTaskWait =>
            "Blocking on tasks can deadlock or stall the main thread.\n\n" +
            "**How to fix:** `await` the task; propagate async through the call chain.",
        CodeSmellKind.WeakTemporization =>
            "Frame-rate dependent updates behave differently across devices.\n\n" +
            "**How to fix:** multiply motion by `Time.deltaTime` (or fixed delta in FixedUpdate).",
        CodeSmellKind.ExpensiveUnityApiInHotPath =>
            "Per-frame Unity lookups (GetComponent, Find, Camera.main) add overhead.\n\n" +
            "**How to fix:** cache references in Awake/Start; avoid Find in Update.",
        CodeSmellKind.LinqInHotPath =>
            "LINQ in per-frame methods allocates enumerators and intermediate collections.\n\n" +
            "**How to fix:** replace with explicit loops and reusable buffers.",
        CodeSmellKind.CollectionAllocationInHotPath =>
            "Allocating collections every frame increases GC pressure.\n\n" +
            "**How to fix:** reuse lists/arrays or pre-allocate outside hot paths.",
        CodeSmellKind.StringConcatenationInHotPath =>
            "String building in hot paths allocates temporary strings.\n\n" +
            "**How to fix:** use `StringBuilder`, cached strings, or numeric formatting buffers.",
        _ => null,
    };

    public static IReadOnlyList<string> GetSarifTags(CodeSmellKind kind) => kind switch
    {
        CodeSmellKind.GodClass or CodeSmellKind.LongMethod or CodeSmellKind.ExcessiveParameters
            or CodeSmellKind.HighComplexity or CodeSmellKind.DeepNesting or CodeSmellKind.LowCohesion
            or CodeSmellKind.HighCoupling or CodeSmellKind.LowMaintainability or CodeSmellKind.CyclicDependency
            or CodeSmellKind.DeepInheritance =>
            ["maintainability"],
        CodeSmellKind.BoxingAllocation or CodeSmellKind.ClosureCapture or CodeSmellKind.ParamsArrayAllocation =>
            ["performance", "gc-pressure"],
        CodeSmellKind.CatchAllException or CodeSmellKind.MissingInnerException
            or CodeSmellKind.ThrowingSystemException =>
            ["reliability", "exceptions"],
        CodeSmellKind.ExpensiveUnityApiInHotPath or CodeSmellKind.LinqInHotPath
            or CodeSmellKind.CollectionAllocationInHotPath or CodeSmellKind.StringConcatenationInHotPath
            or CodeSmellKind.WeakTemporization =>
            ["performance", "unity"],
        CodeSmellKind.AsyncVoidMethod or CodeSmellKind.BlockingTaskWait =>
            ["reliability", "async"],
        _ => ["maintainability"],
    };

    static void AppendMetricsLine(StringBuilder sb, string name, string warning, string? critical = null)
    {
        var criticalSuffix = critical is null ? "" : $"     (Critical: {critical})";
        sb.AppendLine($"      {name,-20} {warning}{criticalSuffix}");
    }

    static void AppendDocsRow(StringBuilder sb, string smell, string warning, string critical)
        => sb.AppendLine($"| {smell} | {warning} | {critical} |");
}
