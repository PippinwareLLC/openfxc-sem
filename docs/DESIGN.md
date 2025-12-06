# ✅ **1. Semantic analysis is purely about *HLSL meaning***

The semantic analyzer’s job is:

### ✔ Build symbol tables

* Functions
* Parameters
* Variables
* Structs
* Samplers / Textures
* CBuffers / TBuffers

### ✔ Compute types

* Type of every expression
* Type of every value
* Type of intrinsic calls (`mul`, `dot`, `tex2D`)

### ✔ Bind semantics

* `POSITION0` → semantic name + index
* `SV_Target` → semantic name + index
* Nothing about registers or DXBC locations

### ✔ Select intrinsic overloads

Based purely on HLSL type rules.

### ✔ Produce a typed, validated representation

Independent of hardware targets.

### ❌ It should NOT:

* Choose DX9 registers (`v0`, `r0`, `oC0`…)
* Choose DX9 opcodes (`dp3`, `mov`, `mad`, `texld`)
* Enforce DX9 hardware limits
* Do DX9 register packing
* Do profile-specific legalization beyond trivial constraints
* Think about DXBC chunks, signatures, or operand tokens

Those belong in the backend.

If the semantic analyzer “smells like DX9,” something is wrong.

---

# 🧠 **2. Why DX9 bytecode concerns must be downstream**

## DX9 bytecode choices depend on:

* Register allocation (mid/end stage)
* Instruction selection (backend)
* Shader Model profile legalization
  (`ps_2_0` has no dynamic branching, `ps_3_0` does)
* DXBC encoding rules (binary format)

**None of these are part of HLSL semantics.**

Example:

```hlsl
float4 main(float4 pos : POSITION) : SV_Position
{
    return pos;
}
```

Semantics analyzer sees:

* A function
* A parameter of type `float4` with semantic POSITION
* A return of type `float4` with semantic SV_Position

What it should NOT decide:

* `pos` → `v0`
* output → `o0`
* emit `dcl_position v0`
* emit `dcl_position o0`
* emit `mov o0, v0`

Those are backend artifacts, NOT semantic artifacts.

---

# 🏗 **3. Correct separation of responsibilities in OpenFXC**

Here is the proper pipeline:

```
openfxc-hlsl parse
      ↓
openfxc-sem analyze
      ↓
openfxc-ir lower
      ↓
openfxc-ir optimize
      ↓
openfxc-profile legalize       (Shader Model hardware rules)
      ↓
openfxc-dx9 lower              (Instruction selection)
      ↓
openfxc-dxbc emit              (Bytecode)
```

### Semantic analyzer (`openfxc-sem`):

Pure **HLSL meaning**.
Zero hardware knowledge.

### Profile legalizer (`openfxc-profile`):

Enforce things like:

* SM2 instruction count
* SM2 flow control limits
* SM2 gradient restrictions (`ddx`, `ddy`)
* SM2 vs SM3 texture instruction limits
  Still not DXBC-specific.

### DX9 lowering (`openfxc-dx9`):

* Map IR ops → DX9 opcodes
* Map semantics → abstract I/O registers
* Choose `dp3` vs `dp4` vs `mad` sequences
  Still not bytecode.

### DXBC emitter (`openfxc-dxbc`):

* Convert DX9 logical instructions into binary chunks
* Build ISGN, OSGN, SHDR, RDEF
* Output final bytecode

This separation gives you:

* Clean compiler passes
* Testability per stage
* Compatibility with future backends (DXBC SM4/SM5, DXIL, SPIR-V)

---

# 📌 **4. So what *should* semantic analysis check that relates to profiles?**

A tiny amount — but very carefully.

Semantic analyzer may check:

### ✔ Things tied to the language, not bytecode:

* You cannot use a pixel-shader-only intrinsic in a vertex shader (HLSL-level rule).
* SV semantics make sense for shader stage (e.g. SV_Position on VS output).
* Missing entry points.

### ❌ It should not check:

* Whether DX9 supports that intrinsic — e.g. `ddx` is illegal in VS, but *legalization* decides what to do with IR, not the semantic analyzer.
* Register space limits.
* Instruction count.
* Temp register count.
* Flow control hardware rules.

Those belong to *profile legalization*.

---

# 🏁 **5. Final decision**

It should care ONLY about HLSL semantics and produce a backend-agnostic semantic model.

All DX9-specific behavior happens *later*, in:

* profile legalization
* DX9 lowering
* DXBC emission

If OpenFXC keeps this separation, you will have:

* A clean compiler architecture
* A reusable front-end for future backends
* Better test coverage
* Easier debugging
* Less coupling
* Ability to support SM4/SM5 properly
* Ability to generate IR for DXIL / SPIR-V someday

---

