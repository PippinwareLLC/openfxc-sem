# Semantic Analyzer (openfxc-sem)

Technical reference for end users of the OpenFXC semantic analyzer.

## Purpose
- Consume `openfxc-hlsl` AST JSON (SM1–SM5, FX syntax).
- Build a backend-agnostic semantic model: symbols, types, semantics, entry points, diagnostics.
- Preserve partial results on errors; never crash on malformed input.

## Usage
### CLI
```
openfxc-sem analyze --profile <vs_2_0|ps_5_0|...> [--entry <name>] [--input <path>] < input.ast.json > output.sem.json
```
- `--profile` is required; `--entry` defaults to `main`.
- Input must be the AST JSON produced by `openfxc-hlsl parse`.

### Library
```csharp
var astJson = File.ReadAllText("shader.ast.json"); // from openfxc-hlsl
var analyzer = new SemanticAnalyzer(profile: "ps_4_0", entry: "main", astJson);
var output = analyzer.Analyze();
var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
```
- Package: `src/OpenFXC.Sem.Core/OpenFXC.Sem.Core.csproj` (`OpenFXC.Sem.Core.dll`).
- CLI wrapper: `src/openfxc-sem/openfxc-sem.csproj`.

## Output schema (semantic JSON)
- `formatVersion`: integer, currently `2`.
- `profile`: selected profile string (e.g., `ps_4_0`).
- `syntax`: `{ rootId }` referencing the AST root node id.
- `symbols`: functions, parameters, locals, globals, structs, resources, cbuffers/tbuffers, structured/RW buffers; includes semantics and parent links.
- `types`: normalized types per node id (scalars, vectors, matrices, arrays, resources, functions).
- `entryPoints`: resolved entry with stage derived from profile and symbol id.
- `techniques`: FX metadata: techniques/passes, shader bindings per stage (entry/profile), and render-state assignments.
- `diagnostics`: `severity/id/message/span` (span is start/end in source; clamped to document length).

## Responsibilities & Rules
- **Symbols**: collect functions/params/locals/globals/structs/resources; capture cbuffer/tbuffer members (SM4/5), structured/RW buffers (SM5).
- **Types**: structured `SemType` model; constructors validated for arity/shape; arrays preserve declarators; expression typing for swizzles, indexing, binaries, casts, calls.
- **Intrinsics**:
  - Core: `mul`, `dot`, `normalize`, `saturate`, `tex2D` (+ projected), `sin`, `cos`, `abs`, `length`, `cross`, `min/max/clamp/lerp`, `ddx/ddy` (SM4+).
  - Arity/type-checked; HLSL2001 on mismatch.
- **Semantics**:
  - Legacy semantics uppercased/normalized; indices parsed when present.
  - SM<4: SV_* blocked on params/returns (stage-aware); pixel returns must be COLOR/DEPTH; diagnostics HLSL3002/3003/3004 as applicable.
  - SM4/5: SV_* validated per stage (e.g., VS return SV_Position; PS return SV_Target/Depth/Coverage; PS params may use SV_Position/SV_PrimitiveID/SV_SampleIndex/IsFrontFace/RTArrayIndex).
- **Entry points**: resolved by name (or fallback with diagnostic), stage inferred from profile.
- **FX stance**: techniques/passes are parsed into `techniques[]` (bindings + states). Diagnostics cover missing VS/PS bindings, duplicate technique/pass names, and profile/stage mismatches on bindings.
- **Profiles**: SM1–SM5 supported; stage derived from profile prefix (vs/ps/gs/hs/ds/cs). Profile info is carried through symbols/types/entryPoints.
- **Diagnostics** (IDs stable):
  - HLSL100x: duplicates (e.g., duplicate function).
  - HLSL2001: invalid call/constructor/intrinsic.
  - HLSL2002: binary type mismatch.
  - HLSL2005: unknown identifier.
  - HLSL3001: missing entry point.
  - HLSL3002/3003/3004: semantic/stage/profile issues (invalid/system-value/duplicate/missing).
  - HLSL5001–HLSL5006: FX issues (missing technique name, duplicate technique/pass, missing VS/PS, stage/profile mismatch).

## Testing
- Unit/negative/intrinsic/semantics/FX tests: `dotnet test tests/OpenFXC.Sem.Tests/OpenFXC.Sem.Tests.csproj`
- Snapshots (semantic JSON): `tests/snapshots/*.sem.json` (SM2 VS/PS, SM4 cbuffer, SM5 structured, SM2 invalid SV case, FX technique coverage).
- DXSDK sweep (env-gated): `OPENFXC_SEM_FX_SWEEP=all dotnet test ...` to run all `samples/dxsdk/**/*.fx`; default runs only a single sample for speed.

## Notes
- Input must come from `openfxc-hlsl`; do not synthesize ASTs manually.
- The analyzer is backend-agnostic: no register packing/opcode selection/DXBC concerns; outputs are suitable for downstream IR/profile/lowering passes.
- Partial models are emitted on errors; spans are normalized to source length.
