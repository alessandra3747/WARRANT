# WARRANT - AI Agent Grounding Certification Gate

WARRANT is a cryptographic go/no-go gate that certifies whether a given dataset is currently safe for an AI agent to use for grounding.

It reduces the risk of grounding on stale, incomplete or tampered data by giving AI agents cryptographically signed, time-bound contracts that are verified in real time through the Model Context Protocol.

## ⚙️ System Architecture

WARRANT runs on a distributed cloud architecture with an emphasis on zero-trust verification and an adaptive, license-aware signal model, built on native Microsoft services:

* **Azure Functions:** The computational core and orchestrator. Runs the asynchronous certification lifecycle and a swarm of "guardians" that inspect the data, with event sourcing for a replayable audit trail.
* **Azure OpenAI:** The reasoning layer. Scores hallucination risk from the schema and a data sample, returning explainable findings (undefined fields, ambiguous codes, out-of-range values).
* **Azure Key Vault:** The cryptographic layer. Signs each verdict with an asymmetric EC P-256 key, producing a tamper-evident JWS contract.
* **Microsoft Dataverse:** The source under evaluation and the contract store, holding both the assessed data and the issued certificates.
* **MCP Gate:** A lightweight interface exposing the `CheckAssetForTask` tool to clients such as Microsoft Copilot Studio, verifying contract signatures on every call.

The core runs standalone on Dataverse, Azure OpenAI and Key Vault. Native governance signals (Purview DLP, Defender SPM, Agent 365, Fabric IQ) are consumed through a single interface **only when the client is licensed**, degrading gracefully to a self-computed floor otherwise. Each contract records exactly which signals were Native and which were Floor.

## ⚙️ Gate Flow and Modes

The **verdict** is WARRANT's assessment of the data, written into the signed contract: `Ready`, `Conditional`, or `No`.

The **mode** is the gate's live instruction to the agent, computed from the verdict *plus* signature verification, validity and finding type. The same verdict can map to different modes which is why a quality `No` differs from a security `No` and a `Ready` from a tampered row is still rejected.

The gate returns one of four modes:

1. **full:** Data is trusted, complete and safe. The agent uses it without restrictions.
2. **restricted:** Quality issues detected (e.g. missing definitions, bad formats, low completeness, duplicates). The agent may still answer, but should avoid the flagged fields (`avoidFields`) and include the provided caveat in its response.
3. **blocked:** Hard usage block. Triggered by a security finding or by a **signature/integrity failure**, including a signed contract altered in Dataverse.
4. **pending:** The contract is missing or expired. The gate blocks usage and triggers an on-demand certification in Azure Functions, leading the agent to retry shortly.

## 🔒 Security & Zero-Trust

WARRANT does not blindly trust database rows and the signed payload is the source of truth. On every gate call:

* The gate retrieves the contract and its JWS from Dataverse.
* It verifies the signature mathematically against the public key in Key Vault, confirming the payload was signed by WARRANT and not altered.
* It reads the verdict from the **signed payload** and compares it against the `wrnt_verdict` column. Any discrepanc results in an immediate **blocked** status.

This makes tampering detectable: changing a verdict directly in the database does not fool the gate, because the signed payload no longer matches the row.

## ⚙️ Continuous Re-Validation

Every contract carries a validity window. The orchestrator re-certifies the same stream on a timer and on demand, reusing a deterministic contract id so the record is refreshed and versioned rather than duplicated. When the data is unchanged, re-certification skips the LLM to save cost. When it changes, the verdict can flip and a previously `Ready` dataset can become `Conditional` or `No`.

## 🚀 Next Steps

The current PoC intentionally focuses on Dataverse and deliberately minimal floor signals. The interface-driven design allows for seamless expansion:

* **SharePoint Loader:** Certify document libraries through a new `IAssetLoader`, with the core unchanged.
* **Native Signals, Live:** Wire Purview DLP, Defender SPM and Agent 365 through `IExternalSignalSource`, flip a capability flag and a signal moves from Floor to Native.
* **Offline Signature Check:** Clients verify the JWS against a cached public JWK locally, removing the need to query Key Vault on every request for fully decoupled zero-trust.

## Click below to watch the WARRANT Demo uploaded to Youtube (in order to maintain video quality)
[![WARRANT Demo](https://img.youtube.com/vi/IGT9o-cB5R4/maxresdefault.jpg)](https://youtu.be/IGT9o-cB5R4?si=jDWerEvg2ROi8F36)
