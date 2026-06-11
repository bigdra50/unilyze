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

    static void AppendMetricsLine(StringBuilder sb, string name, string warning, string? critical = null)
    {
        var criticalSuffix = critical is null ? "" : $"     (Critical: {critical})";
        sb.AppendLine($"      {name,-20} {warning}{criticalSuffix}");
    }

    static void AppendDocsRow(StringBuilder sb, string smell, string warning, string critical)
        => sb.AppendLine($"| {smell} | {warning} | {critical} |");
}
