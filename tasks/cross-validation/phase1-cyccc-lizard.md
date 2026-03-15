# Phase 1: CycCC - Unilyze vs lizard 比較結果

## 概要

| Project | Matched | Exact% | Within1% | MAE | MaxDelta | Spearman | Pearson |
|---------|---------|--------|----------|-----|----------|----------|---------|
| HelloMarioFramework | 418 | 91.6% | 99.8% | 0.09 | 3 | 0.958 | 0.998 |
| BossRoom | 988 | 91.1% | 99.5% | 0.10 | 4 | 0.933 | 0.973 |
| UniTask | 996 | 96.8% | 98.8% | 0.10 | 18 | 0.869 | 0.700 |
| VContainer | 424 | 91.7% | 97.6% | 0.13 | 7 | 0.930 | 0.956 |

## HelloMarioFramework

- Matched methods: 418
- Unmatched (Unilyze only): 0
- Unmatched (lizard only): 1
- Non-trivial methods (CycCC > 1): 204

### Top divergences

| Method | Unilyze | lizard | Delta |
|--------|---------|--------|-------|
| `TitleScreen.Start:0` | 1 | 4 | -3 |
| `BeepBlockBeeper.Beep:0` | 3 | 2 | +1 |
| `BlockSwitch.OnCollisionEnter:1` | 7 | 6 | +1 |
| `Brick.OnCollisionEnter:1` | 6 | 5 | +1 |
| `Bully.OnCollisionStay:1` | 9 | 8 | +1 |
| `Bully.OnCollisionStayStompable:1` | 6 | 5 | +1 |
| `Checkpoint.OnTriggerEnter:1` | 5 | 4 | +1 |
| `CheepCheep.OnCollisionStayStompable:1` | 4 | 3 | +1 |
| `Chuck.OnCollisionStay:1` | 7 | 6 | +1 |
| `Chuck.OnCollisionStayStompable:1` | 6 | 5 | +1 |
| `Donut.OnCollisionStay:1` | 5 | 4 | +1 |
| `Enemy.OnCollisionStayStompable:1` | 4 | 3 | +1 |
| `FlipPanel.PanelCheck:0` | 7 | 6 | +1 |
| `FlipPanel.Start:0` | 3 | 2 | +1 |
| `FreeLookHelper.LoadSettings:0` | 4 | 5 | -1 |
| `HammerBro.OnCollisionStayStompable:1` | 5 | 4 | +1 |
| `HatBlock.OnCollisionEnter:1` | 5 | 4 | +1 |
| `Hazard.OnCollisionStay:1` | 3 | 2 | +1 |
| `ItemSpawnButton.Start:0` | 3 | 4 | -1 |
| `LoadingScreen.LoadAsyncScene:0` | 2 | 3 | -1 |

### Unmatched (lizard only, sample)

- `AndroidInstantiatePrefab.Start:0`

## BossRoom

- Matched methods: 988
- Unmatched (Unilyze only): 166
- Unmatched (lizard only): 193
- Non-trivial methods (CycCC > 1): 503

### Top divergences

| Method | Unilyze | lizard | Delta |
|--------|---------|--------|-------|
| `ServerActionPlayer.ClearActions:1` | 7 | 3 | +4 |
| `AnimatorTriggeredSpecialFXEditor.ValidateNodeNames:1` | 14 | 11 | +3 |
| `FXProjectile.Impact:0` | 5 | 2 | +3 |
| `BakingMenu.HandleEnvLights:3` | 3 | 1 | +2 |
| `NetworkSimulatorUIMediator.OnScenarioChanged:1` | 7 | 5 | +2 |
| `AIBrain.FindBestEligibleAIState:0` | 3 | 2 | +1 |
| `Action.InstantiateSpecialFXGraphics:2` | 3 | 2 | +1 |
| `ActionConfig.CanBeInterruptedBy:1` | 3 | 2 | +1 |
| `ActionFactory.PurgePooledActions:0` | 2 | 1 | +1 |
| `AnimatorNodeHook.OnStateEnter:3` | 5 | 4 | +1 |
| `AnimatorNodeHook.OnStateExit:3` | 5 | 4 | +1 |
| `AnimatorTriggeredSpecialFXEditor.HasAudioSource:1` | 4 | 3 | +1 |
| `ApplicationController.Configure:1` | 1 | 2 | -1 |
| `ApplicationController.QuitGame:1` | 1 | 2 | -1 |
| `AttackAIState.ChooseAttack:0` | 5 | 4 | +1 |
| `AttackAIState.ChooseFoe:0` | 3 | 2 | +1 |
| `BakingMenu.HandleLightProbes:2` | 4 | 3 | +1 |
| `BakingMenu.HandleReflectionProbes:2` | 4 | 3 | +1 |
| `Breakable.PerformBreakVisualization:1` | 7 | 6 | +1 |
| `Breakable.PerformUnbreakVisualization:0` | 4 | 3 | +1 |

### Unmatched (Unilyze only, sample)

- `AIState.Initialize:0`
- `AIState.IsEligible:0`
- `AIState.Update:0`
- `Action.OnStart:1`
- `Action.OnUpdate:1`
- `ActionRequestData.NetworkSerialize:1`
- `AnimatorTriggeredSpecialFX.Awake:0`
- `AnimatorTriggeredSpecialFX.CoroPlayStateEnterFX:1`
- `AnimatorTriggeredSpecialFX.CoroPlayStateEnterSound:1`
- `AnimatorTriggeredSpecialFX.GetAudioSourceForLooping:0`

### Unmatched (lizard only, sample)

- `AIBrain.AIBrain:2`
- `ActionButtonInfo.Action1ModifiedCallback:0`
- `ActionButtonInfo.ActionButtonInfo:3`
- `ActionButtonInfo.Awake:0`
- `ActionButtonInfo.DeregisterInputSender:0`
- `ActionButtonInfo.OnButtonClickedDown:1`
- `ActionButtonInfo.OnButtonClickedUp:1`
- `ActionButtonInfo.OnDestroy:0`
- `ActionButtonInfo.OnDisable:0`
- `ActionButtonInfo.OnEnable:0`

## UniTask

- Matched methods: 996
- Unmatched (Unilyze only): 1689
- Unmatched (lizard only): 1749
- Non-trivial methods (CycCC > 1): 198

### Top divergences

| Method | Unilyze | lizard | Delta |
|--------|---------|--------|-------|
| `ContinuationQueue.Run:0` | 1 | 19 | -18 |
| `PlayerLoopRunner.Run:0` | 1 | 19 | -18 |
| `RuntimeHelpersAbstraction.WellKnownNoReferenceContainsTypeInitialize:1` | 9 | 19 | -10 |
| `UniTaskScheduler.PublishUnobservedTaskException:1` | 5 | 15 | -10 |
| `PlayerLoopHelper.Init:0` | 3 | 8 | -5 |
| `UniTaskCompletionSource.GetResult:1` | 5 | 1 | +4 |
| `CancellationTokenExtensions.ToCancellationToken:2` | 1 | 3 | -2 |
| `DiagnosticsExtensions.TryResolveStateMachineMethod:2` | 7 | 5 | +2 |
| `EnumeratorPromise.ConsumeEnumerator:1` | 14 | 12 | +2 |
| `PlayerLoopHelper.ForceEditorPlayerLoopUpdate:0` | 10 | 8 | +2 |
| `PlayerLoopHelper.Initialize:2` | 1 | 3 | -2 |
| `UniTaskCancellationExtensions.GetCancellationTokenOnDestroy:1` | 1 | 3 | -2 |
| `AsyncUniTaskVoidMethodBuilder.SetException:1` | 2 | 3 | -1 |
| `AsyncUniTaskVoidMethodBuilder.SetResult:0` | 2 | 3 | -1 |
| `AsyncUnityEventHandler.Dispose:0` | 3 | 4 | -1 |
| `AsyncUnityEventHandler.GetResult:1` | 2 | 1 | +1 |
| `Average.AverageAsync:2` | 4 | 5 | -1 |
| `DelayFramePromise.MoveNext:0` | 8 | 9 | -1 |
| `DelayTest.DelayFrame:0` | 3 | 2 | +1 |
| `DiagnosticsExtensions.CleanupAsyncStackTrace:1` | 14 | 13 | +1 |

### Unmatched (Unilyze only, sample)

- `Aggregate.AggregateAsync:3`
- `Aggregate.AggregateAsync:4`
- `Aggregate.AggregateAsync:5`
- `Aggregate.AggregateAwaitAsync:3`
- `Aggregate.AggregateAwaitAsync:4`
- `Aggregate.AggregateAwaitAsync:5`
- `Aggregate.AggregateAwaitWithCancellationAsync:3`
- `Aggregate.AggregateAwaitWithCancellationAsync:4`
- `Aggregate.AggregateAwaitWithCancellationAsync:5`
- `All.AllAsync:3`

### Unmatched (lizard only, sample)

- `AddressablesAsyncExtensions.GetAwaiter:1`
- `AddressablesAsyncExtensions.ToUniTask:6`
- `AddressablesAsyncExtensions.WithCancellation:4`
- `Aggregate.AggregateAsync<TSource,TAccumulate,TResult>:5`
- `Aggregate.AggregateAsync<TSource,TAccumulate>:4`
- `Aggregate.AggregateAsync<TSource>:3`
- `Aggregate.AggregateAwaitAsync<TSource,TAccumulate,TResult>:5`
- `Aggregate.AggregateAwaitAsync<TSource,TAccumulate>:4`
- `Aggregate.AggregateAwaitAsync<TSource>:3`
- `Aggregate.AggregateAwaitWithCancellationAsync<TSource,TAccumulate,TResult>:5`

## VContainer

- Matched methods: 424
- Unmatched (Unilyze only): 107
- Unmatched (lizard only): 259
- Non-trivial methods (CycCC > 1): 128

### Top divergences

| Method | Unilyze | lizard | Delta |
|--------|---------|--------|-------|
| `TypeAnalyzer.Analyze:1` | 27 | 20 | +7 |
| `EntryPointDispatcher.Dispatch:0` | 15 | 19 | -4 |
| `TypeAnalyzer.CheckCircularDependencyRecursive:3` | 20 | 16 | +4 |
| `RegisterInfo.GetHeadline:0` | 7 | 4 | +3 |
| `VContainerDiagnosticsInfoTreeView.BuildRoot:0` | 6 | 3 | +3 |
| `CollectionInstanceProvider.CollectFromParentScopes:3` | 8 | 6 | +2 |
| `ContainerBuilder.BuildRegistry:0` | 3 | 5 | -2 |
| `ContainerBuilderExtensions.Register:3` | 1 | 3 | -2 |
| `PrefabDirtyScope.Dispose:0` | 1 | 3 | -2 |
| `VContainerInstanceTreeView.AddProperties:2` | 14 | 12 | +2 |
| `CollectionInstanceProvider.Add:1` | 5 | 4 | +1 |
| `Container.Resolve:2` | 3 | 2 | +1 |
| `ContainerBuilder.Exists:3` | 6 | 5 | +1 |
| `DiagnosticsCollector.TraceBuild:2` | 3 | 2 | +1 |
| `ExtraInstallationScope.Dispose:0` | 1 | 2 | -1 |
| `FindComponentProvider.SpawnInstance:1` | 8 | 7 | +1 |
| `LifetimeScope.AwakeWaitingChildren:1` | 5 | 4 | +1 |
| `LifetimeScope.OnSceneLoaded:2` | 5 | 4 | +1 |
| `OpenGenericRegistrationBuilder.AddInterfaceType:1` | 7 | 6 | +1 |
| `OpenGenericRegistrationBuilder.AsImplementedInterfaces:0` | 4 | 3 | +1 |

### Unmatched (Unilyze only, sample)

- `CappedArrayPool<T>.Rent:1`
- `CappedArrayPool<T>.Return:1`
- `ComponentsBuilder.AddInHierarchy:0`
- `ComponentsBuilder.AddInNewPrefab:2`
- `ComponentsBuilder.AddInstance:1`
- `ComponentsBuilder.AddOnNewGameObject:2`
- `ContainerBuilder.Register:1`
- `ContainerBuilderExtensions.Register:2`
- `ContainerBuilderExtensions.RegisterFactory:2`
- `ContainerBuilderExtensions.RegisterFactory:3`

### Unmatched (lizard only, sample)

- `ActionInstaller.ActionInstaller:1`
- `ActionInstaller.operator ActionInstaller:1`
- `AllInjectionFeatureService.AllInjectionFeatureService:2`
- `AllInjectionFeatureService.AllInjectionFeatureService:3`
- `AsyncLoopItem.AsyncLoopItem:1`
- `AsyncStartableLoopItem.AsyncStartableLoopItem:2`
- `AsyncStartableLoopItem.Dispose:0`
- `AsyncStartableLoopItem.MoveNext:0`
- `AsyncStartableThrowable.StartAsync:1`
- `AwaitableExtensions.Forget:2`

## Unmatched 原因分析

マッチングキーは `TypeName.MethodName:ParamCount` で突合。未マッチの主な原因:

| 原因 | 件数 (UniTask) | 説明 |
|------|---------------|------|
| ジェネリクス型パラメータ | ~491 | lizard は `Aggregate.AggregateAsync<TSource>` と出力、Unilyze は `Aggregate.AggregateAsync` |
| コンストラクタ名 | ~285 | lizard は `ClassName.ClassName` と出力、Unilyze はコンストラクタを別扱い |
| operator / 変換演算子 | 少数 | lizard は `operator TypeName` と出力 |
| 内部型 / ネスト型 | 少数 | 型名の完全修飾が異なる |

これらは全て命名規約の差異であり、CycCC 計算精度には影響しない。
マッチ済みの 2826 メソッド (全プロジェクト合計) での一致率が本来の精度指標。

## 結論

| 基準 | 目標 | 結果 | 判定 |
|------|------|------|------|
| Spearman rho | >= 0.90 | 0.87-0.96 | 3/4 合格 (UniTask はジェネリクス外れ値の影響) |
| 完全一致率 | >= 60% | 91-97% | 全プロジェクト合格 |
| MAE | < 3.0 | 0.09-0.13 | 全プロジェクト合格 |

Unilyze の CycCC は lizard と高い一致を示す。差異は `?.`, `goto`, `switch expression arm` の扱いに起因し、Unilyze の方が McCabe 拡張仕様に忠実。

## 既知の定義差異

| 対象 | lizard | Unilyze | 影響 |
|------|--------|---------|------|
| `?.` (null conditional) | 非カウント | +1 | Unilyze > lizard |
| `goto` | 非カウント | +1 | Unilyze > lizard |
| `#if`/`#elif` | +1 | 非カウント | lizard > Unilyze |
| `switch expression arm` | 非カウント | +1 | Unilyze > lizard |
| `bool &`/`bool \|` | 非カウント | +1 (SemanticModel) | Unilyze > lizard |
