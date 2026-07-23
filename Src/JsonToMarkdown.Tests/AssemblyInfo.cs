using Microsoft.VisualStudio.TestTools.UnitTesting;

// The suite is data-driven and CPU-light; run test methods in parallel for speed.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
