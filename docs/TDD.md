# TDD – `openfxc-sem` (Semantic Analyzer for OpenFXC)

## 0. Overview

This document defines the **Test-Driven Development spec** for the **`openfxc-sem`** component of OpenFXC.

`openfxc-sem` is the **semantic analysis** stage that sits immediately after **`openfxc-hlsl`** (lexer/parser) and before IR lowering.

**Pipeline context:**

```text
openfxc-hlsl parse  →  openfxc-sem analyze  →  openfxc-ir lower  →  ...
   (tokens + AST)        (semantic model)        (IR module)
```

---

## 1. Scope & Goals

### 1.1 Goals

`openfxc-sem` shall:

1. Take as input the **HLSL syntax tree JSON** emitted by `openfxc-hlsl parse`.
2. Build a **semantic model** for HLSL covering SM1–SM5 era features, including:

   * Symbol table (functions, parameters, variables, structs, resources, cbuffers, etc.)
   * Type information for all expressions and declarations
   * Intrinsic resolution (e.g., `mul`, `dot`, `tex2D`, `normalize`, etc.)
   * Semantic binding (`POSITION`, `COLOR`, `SV_Position`, etc.)
3. Produce:

   * A **semantic JSON document** suitable for IR lowering
   * A stable set of **diagnostics** (errors/warnings)
4. Never crash on input; always emit a model (even partial) and diagnostics.

### 1.2 Non-goals

`openfxc-sem` does **not**:

* Generate IR or DXBC.
* Do optimization or control-flow analysis.
* Fully enforce shader-model-specific hardware limits (that’s a later profile/legalization stage).
* Evaluate the preprocessor (`#if`, `#define`), beyond what is already represented in the AST from `openfxc-hlsl`.

---

## 2. CLI Contract

### 2.1 Command

```bash
openfxc-sem analyze [options] < input.ast.json > output.sem.json
```

**Required input**:
JSON AST produced by:

```bash
openfxc-hlsl parse file.hlsl --format json > file.ast.json
```

**Key CLI options:**

* `--profile <name>`

  * Required. The intended shader profile:
  * Examples: `vs_2_0`, `ps_2_0`, `vs_3_0`, `ps_3_0`, `vs_5_0`, `ps_5_0`.
* `--entry <name>` (optional)

  * Explicit entry point name (default: `main`).

### 2.2 Exit codes

| Code | Meaning                                           |
| ---- | ------------------------------------------------- |
| 0    | Semantic analysis completed (diagnostics allowed) |
| 1    | Internal error (e.g., I/O, JSON parse failure)    |

---

## 3. Input / Output Schemas (High-Level)

### 3.1 Input (from `openfxc-hlsl`)

The input must be the AST JSON as defined in `TDD-openfxc-hlsl.md`, containing:

* `root` – `HlslCompilationUnit` syntax node
* `tokens` – flat token list
* `diagnostics` – lexer/parser diagnostics (may be non-empty)
* `source` metadata

`openfxc-sem` must tolerate:

* Non-empty `diagnostics` (but may refuse to analyze certain fatal syntax failures if needed).

### 3.2 Output (semantic model)

**Top-level JSON structure (conceptual):**

```json
{
  "formatVersion": 1,
  "profile": "vs_2_0",
  "syntax": {
    "rootId": 1
  },
  "symbols": [
    {
      "id": 1,
      "kind": "Function",
      "name": "main",
      "type": "float4(float4)",
      "declNodeId": 10
    },
    {
      "id": 2,
      "kind": "Parameter",
      "name": "pos",
      "type": "float4",
      "semantic": { "name": "POSITION", "index": 0 },
      "declNodeId": 12,
      "parentSymbolId": 1
    }
  ],
  "types": [
    {
      "nodeId": 20,
      "type": "float4"
    }
  ],
  "entryPoints": [
    {
      "name": "main",
      "symbolId": 1,
      "stage": "Vertex",
      "profile": "vs_2_0"
    }
  ],
  "diagnostics": [
    {
      "severity": "Error",
      "id": "HLSL2001",
      "message": "Cannot convert from 'float' to 'float4'.",
      "span": { "start": 123, "end": 124 }
    }
  ]
}
```

Implementation details can vary, but tests must assert:

* There is a symbol table.
* Each relevant AST node (expressions/declarations) can be mapped to a type.
* Entry points are determined and recorded.
* All diagnostics are collected and returned.

---

## 4. Testing Strategy

All tests for `openfxc-sem` should be based on:

* **AST input samples** produced by `openfxc-hlsl parse`
* **Expected semantic model** JSON (golden/snapshot tests)
* **Diagnostic expectations** (type mismatches, unknown names, etc.)

### 4.1 Types of tests

* **Unit tests** – symbol table building, type inference for specific expression patterns.
* **Snapshot tests** – for full shaders, compare the entire semantic JSON to a stored “golden” file.
* **Negative tests** – ill-typed shaders, missing functions, invalid casts.
* **Profile-aware tests** – `vs_2_0` vs `ps_5_0` differences in allowed constructs (to the extent semantics enforces them).

---

## 5. Semantic Responsibilities & Detailed TDD

This section enumerates the semantic responsibilities and establishes what must be tested for SM1–SM5 compatibility.

### 5.1 Symbol Table

**Semantic task:**

* Collect and record all declared symbols:

  * Functions
  * Parameters
  * Local variables
  * Global variables
  * Structs / typedefs
  * CBuffers / TBuffers (SM4+)
  * Samplers / textures / resource objects

**Tests:**

1. **Function symbols**

   Given:

   ```hlsl
   float4 main(float4 pos : POSITION) : SV_Position
   {
       return pos;
   }
   ```

   Expect in `symbols`:

   * Symbol of kind `Function` with:

     * name `main`
     * type `float4(float4)`
     * stage (deduced later from profile or semantics) recorded in `entryPoints`.

2. **Parameters**

   Same shader:

   * Symbol of kind `Parameter` named `pos`, type `float4`, semantic `POSITION0`, parentSymbolId = function’s symbol id.

3. **Global variables**

   ```hlsl
   float4x4 WorldViewProj;
   sampler2D DiffuseSampler;
   ```

   Expect global symbols:

   * `WorldViewProj` with type `float4x4`.
   * `DiffuseSampler` with type `sampler2D` (or equivalent sampler/resource type).

4. **Structs**

   ```hlsl
   struct VSInput {
       float4 pos : POSITION0;
       float2 uv  : TEXCOORD0;
   };
   ```

   Expect a symbol `VSInput` of kind `Struct`, containing member type/semantic info in a struct type definition.

---

### 5.2 Type System & Type Inference

`openfxc-sem` must assign a type to:

* Every expression node
* Every declaration
* Function returns

#### 5.2.1 Scalar, vector, matrix types

Tests:

* `float`, `float2`, `float3`, `float4`, `float4x4`, `int`, `bool`, etc.
* Declarations like:

  ```hlsl
  float a;
  float2 b;
  float4x4 m;
  ```

  must produce types `float`, `float2`, `float4x4` in the semantic model.

#### 5.2.2 Expressions

Tests for:

1. **Binary arithmetic**

   ```hlsl
   float2 a, b;
   float2 c = a + b;
   ```

   * `a + b` must have type `float2`.
   * Diagnostics if mismatched vector sizes (e.g., `float2 + float3`), unless FXC-compatible rules allow it.

2. **Scalar-vector interactions**

   ```hlsl
   float2 a;
   float  s;
   float2 c = a * s;
   ```

   * Type of `a * s` is `float2`.
   * Test also `s * a`.

3. **Matrix multiplication (early HLSL)**

   ```hlsl
   float4x4 m;
   float4 v;
   float4 r = mul(v, m);
   ```

   * `mul(v, m)` should produce `float4`.
   * Semantic rules from early HLSL (row-major/column-major behavior) can be simplified at first but must have deterministic type inference.

4. **Swizzles**

   ```hlsl
   float4 v;
   float2 u = v.xy;
   float  s = v.x;
   ```

   * `v.xy` → type `float2`.
   * `v.x`  → type `float`.

---

### 5.3 Intrinsic & Builtin Function Resolution

`openfxc-sem` must resolve intrinsic calls to a logical builtin function signature.

#### 5.3.1 Tests: arithmetic intrinsics

Examples:

* `dot(float3, float3)` → `float`
* `normalize(float3)` → `float3`
* `saturate(float4)` → `float4`
* `mul(float4x4, float4)` → appropriate result type

For each intrinsic:

* Provide a positive test where arguments are correct.
* Provide at least one negative test (wrong types, wrong number of args) that yields diagnostics.

#### 5.3.2 Tests: texture intrinsics (SM2+)

```hlsl
texture2D DiffuseTex;
sampler2D DiffuseSampler = sampler_state { Texture = <DiffuseTex>; };

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    return tex2D(DiffuseSampler, uv);
}
```

* `tex2D(sampler2D, float2)` must type-check and produce `float4` (or appropriate resource-return type).
* Wrong types (`tex2D(DiffuseSampler, float3)`) must generate a diagnostic.

---

### 5.4 Semantics (POSITION, COLOR, SV_*, etc.)

Semantic analyzer must:

* Extract semantics from parameter and return declarations.
* Normalize them into a structured representation.

Tests:

#### 5.4.1 Legacy semantics

```hlsl
float4 VSMain(float4 pos : POSITION0) : POSITION
```

Expect:

* Parameter `pos`:

  * semantic: `POSITION`, index 0
* Return:

  * semantic: `POSITION`, index 0 (implicit index).

#### 5.4.2 SV_ semantics (SM4+)

```hlsl
float4 VSMain(float4 pos : POSITION) : SV_Position;
float4 PSMain(float4 color : COLOR0) : SV_Target1;
```

Expect:

* Return semantics: `SV_Position` (no numeric index for Position).
* Return semantics: `SV_Target`, index 1.

Tests must check that parser + semantics together interpret:

* Raw syntax (`SV_Position`, `COLOR0`) → semantic name + index.

---

### 5.5 Entry Point Resolution

`openfxc-sem` must identify entry points:

* Default: `main`
* Or the function specified by `--entry`

Tests:

1. **Simple**

   ```hlsl
   float4 main(float4 pos : POSITION) : SV_Position { return pos; }
   ```

   Expect:

   * `entryPoints` contains one entry: function `main`, stage derived from profile (`vs_2_0` → Vertex).

2. **Multiple candidate functions with `--entry`**

   ```hlsl
   float4 VSMain(float4 pos : POSITION) : SV_Position { return pos; }
   float4 PSMain(float4 c : COLOR0) : SV_Target { return c; }
   ```

   * `--entry VSMain --profile vs_2_0` → entryPoints: VSMain.
   * `--entry PSMain --profile ps_2_0` → entryPoints: PSMain.

3. **Missing entry**

   * No function named `main`, and `--entry` not provided:

     * Must emit a diagnostic: “No entry point found.”

---

### 5.6 Basic Semantic Error Cases

Tests must cover at least:

1. **Unknown identifier**

   ```hlsl
   float4 main() : SV_Target
   {
       return notDefined;
   }
   ```

   Expect:

   * Diagnostic: unknown identifier `notDefined`.

2. **Type mismatch**

   ```hlsl
   float4 main() : SV_Target
   {
       float  a = 1.0;
       float4 b = a;
       return b;
   }
   ```

   At minimum:

   * Diagnostic on `float4 b = a;` (“cannot convert from float to float4” or equivalent).

3. **Wrong number of function arguments**

   ```hlsl
   float4 main(float4 pos : POSITION) : SV_Position
   {
       float4 a = float4(1,2,3,4,5);
       return a;
   }
   ```

   * Call to `float4(...)` with 5 args must produce diagnostic.

4. **Duplicate symbol**

   ```hlsl
   float4 main(float4 pos : POSITION) : SV_Position { return pos; }
   float4 main(float4 pos : POSITION) : SV_Position { return pos; }
   ```

   * Diagnostic: duplicate function `main`.

---

### 5.7 Profile-Aware Checks (Semantic Level)

`openfxc-sem` isn’t the full profile/legalization layer, but it can catch blatantly illegal combinations, like:

* Using SV semantics in SM2-only profiles, or vice versa, if you choose to enforce it here.

Tests (at least minimal):

1. **Profile mismatch (optional, if implemented here)**

   ```hlsl
   // analyzed with --profile vs_2_0
   float4 main(float4 pos : SV_Position) : SV_Position { return pos; }
   ```

   * Emit a diagnostic if you decide that SV semantics are inappropriate for SM2 profiles at semantic stage.

If you choose to defer profile legality to a separate tool (e.g., `openfxc-profile`), note that in the implementation, but the tests can still assert that profile info is passed through correctly.

---

## 6. Integration Tests

Using real HLSL snippets (from early DX9 and SM2/SM3 samples):

1. **Simple VS2.0 passthrough**

   ```hlsl
   float4 main(float4 pos : POSITION0) : POSITION0
   {
       return pos;
   }
   ```

   Pipeline:

   ```bash
   openfxc-hlsl parse vs.hlsl > vs.ast.json
   openfxc-sem analyze --profile vs_2_0 < vs.ast.json > vs.sem.json
   ```

   Assert:

   * Exactly one function symbol.
   * One parameter, one return.
   * Entry point resolved.
   * No diagnostics.

2. **SM2 PS with texture**

   ```hlsl
   sampler2D DiffuseSampler;
   float4 main(float2 uv : TEXCOORD0) : COLOR0
   {
       return tex2D(DiffuseSampler, uv);
   }
   ```

   Assert:

   * Symbol for `DiffuseSampler` with sampler type.
   * `tex2D` resolved as intrinsic.
   * Expression type of `tex2D(...)` is `float4`.

3. **SM4/SM5 cbuffer example**

   ```hlsl
   cbuffer PerFrame : register(b0)
   {
       float4x4 ViewProj;
       float3   LightDir;
       float    padding;
   };

   float4 VSMain(float4 pos : POSITION) : SV_Position
   {
       return mul(pos, ViewProj);
   }
   ```

   Assert:

   * CBuffer symbol with fields.
   * `ViewProj` type `float4x4`.
   * `mul` result type `float4`.
   * Entry point `VSMain`.

---

## 7. Definition of Done (DoD)

`openfxc-sem` is considered **ready** when:

1. **CLI contract honored**

   * `openfxc-sem analyze` reads AST JSON from `openfxc-hlsl`.
   * `--profile` and `--entry` are respected.
   * JSON semantic model is emitted.

2. **Symbol table coverage**

   * Functions, parameters, locals, globals, structs, samplers, and (for SM4/5) cbuffers have symbol entries.
   * Tests exist for each category.

3. **Type system coverage**

   * Scalars, vectors, matrices, arrays, and common resource types are inferred correctly.
   * Binary arithmetic and calls to intrinsics yield expected types.

4. **Intrinsic resolution**

   * Core intrinsics (`mul`, `dot`, `normalize`, `saturate`, `tex2D`, etc.) tested.
   * Wrong-usage diagnostics exist and are tested.

5. **Semantics and entry points**

   * Semantics are extracted (`POSITION0`, `COLOR0`, `SV_Position`, `SV_Target`).
   * Entry points are resolved (default `main` or via `--entry`).
   * At least one test per shader stage profile used.

6. **Diagnostics**

   * Unknown identifier, type mismatch, wrong argument count, and duplicate symbol errors are detected and tested.
   * Diagnostics are stable (IDs like `HLSL2xxx`).

7. **Integration with `openfxc-hlsl`**

   * Running `openfxc-hlsl parse` → `openfxc-sem analyze` on a small corpus of shaders (SM2–SM5) yields:

     * No internal failures.
     * Reasonable semantic outputs.
     * Diagnostics only where expected.

---

## 8. Future Extensions (Not Required for Initial DoD)

* Full preprocessor evaluation (macro and conditional compilation).
* More advanced profile checks (e.g., gradient ops legality in SM2).
* Constant folding at semantic stage.
* More complete intrinsic library for SM4/SM5.
* Interop with an LSP for IDE features (hover types, go-to-definition, etc.).

---
