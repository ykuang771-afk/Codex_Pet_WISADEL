# Reference implementation

The independently written local token ledger follows techniques studied in
[CC Switch](https://github.com/farion1231/cc-switch), commit
`1ee2fdc3a791f1e73476c631c7ab7ce8fac0638f`, particularly
`src-tauri/src/services/session_usage_codex.rs` and
`src-tauri/src/proxy/usage/calculator.rs` (MIT license).

The study covered exact request usage, repeated quota snapshots, ordered fork
history exclusion, cached input accounting, and durable database deduplication.
No proxy, credential switching, network interception or CC Switch executable is used.
Reference source and its MIT notice are retained in the project's
`references/cc-switch/` folder.

Prices are independently verified from the official OpenAI model pages linked
in `pricing.json`. Amounts are estimated Standard API token equivalents, not a
subscription invoice. Tool fees, unavailable historical logs and models without
verified public prices are not assigned invented charges.
