"""Shared Kind ↔ UNI rule-id mapping for smell precision corpus scripts."""

from __future__ import annotations

KIND_TO_RULE_ID: dict[str, str] = {
    "GodClass": "UNI001",
    "LongMethod": "UNI002",
    "ExcessiveParameters": "UNI003",
    "HighComplexity": "UNI004",
    "DeepNesting": "UNI005",
    "LowCohesion": "UNI006",
    "HighCoupling": "UNI007",
    "LowMaintainability": "UNI008",
    "CyclicDependency": "UNI009",
    "DeepInheritance": "UNI010",
    "BoxingAllocation": "UNI011",
    "ClosureCapture": "UNI012",
    "ParamsArrayAllocation": "UNI013",
    "CatchAllException": "UNI014",
    "MissingInnerException": "UNI015",
    "ThrowingSystemException": "UNI016",
    "ExpensiveUnityApiInHotPath": "UNI017",
    "LinqInHotPath": "UNI018",
    "CollectionAllocationInHotPath": "UNI019",
    "StringConcatenationInHotPath": "UNI020",
    "WeakTemporization": "UNI021",
    "AsyncVoidMethod": "UNI022",
    "BlockingTaskWait": "UNI023",
}

RULE_ID_TO_KIND = {rule_id: kind for kind, rule_id in KIND_TO_RULE_ID.items()}


def rule_id_for_kind(kind: str) -> str:
    rule_id = KIND_TO_RULE_ID.get(kind)
    if rule_id is None:
        raise KeyError(f"Unknown smell kind: {kind}")
    return rule_id
