namespace Salryn.Tests.SmokeProof;

/// <summary>
/// Stable metadata used by HARDEN evidence checks. Keep this as metadata only; do not let it become
/// a second test framework or a substitute for live operator proof.
/// </summary>
public sealed record SmokeProofRecord(
    string CheckName,
    SmokeProofClass ProofClass,
    string WhatItProves,
    string WhatItDoesNotProve,
    string NextProofNeeded);
