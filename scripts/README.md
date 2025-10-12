# Catga Scripts

This directory contains utility scripts for verifying and benchmarking Catga's reflection optimizations.

## Available Scripts

### VerifyReflectionOptimization.ps1

Verifies that reflection optimization has been properly implemented.

**What it checks:**
- `typeof()` usage in hot paths
- TypeNameCache implementation and usage
- TypedSubscribers implementation and usage  
- TypedIdempotencyCache implementation and usage
- Documentation completeness
- Native AOT compatibility

**Usage:**
```powershell
.\scripts\VerifyReflectionOptimization.ps1
```

**Expected Results:**
- ✅ Hot path files: 0 typeof() calls
- ✅ TypeNameCache.cs exists and is used
- ✅ TypedSubscribers.cs exists and is used
- ✅ All documentation files present
- ✅ No AOT warnings

### BenchmarkReflection.ps1

Runs performance benchmarks comparing `typeof()` vs `TypeNameCache`.

**Usage:**
```powershell
.\scripts\BenchmarkReflection.ps1
```

**Expected Results:**
- TypeNameCache should be 20-30x faster than typeof()
- First call uses reflection (~50ns)
- Subsequent calls use cached value (~2ns)

## Quick Verification

To quickly verify the optimization status:

```powershell
# Check typeof() usage in hot paths
Get-ChildItem -Path src -Recurse -Include *.cs | Select-String "typeof\(" | Where-Object { $_.Path -match "RpcClient|CatgaMediator|DistributedMediator|TracingBehavior" }

# Should return no results if optimization is complete
```

## Performance Tips

1. **TypeNameCache** - Use for frequent type name access in hot paths
2. **TypedSubscribers** - Replace Type dictionaries with static generic storage
3. **Source Generator** - Use `AddGeneratedHandlers()` instead of `ScanCurrentAssembly()`

## Documentation

See the following files for detailed information:
- `/REFLECTION_OPTIMIZATION_SUMMARY.md` - Technical details
- `/REFLECTION_OPTIMIZATION_COMPLETE.md` - Complete project report
- `/docs/guides/source-generator-usage.md` - Source generator guide

## Notes

- These scripts require PowerShell 5.1 or later
- Run from the repository root directory
- Ensure the project is compiled before running verification

