# Security boundaries

DesignSpace is a development preview, not an independently audited document sandbox.

The XAML reader prohibits DTD processing and external entity resolution. Import constructs data, never runtime assemblies or executable markup extensions. JSON sample-data preview resolves simple object paths and does not evaluate expressions or execute JavaScript. The application does not fetch remote document assets in this preview.

Input size, model depth/node count, sample-data size and image-export allocation have explicit limits. These limits do not guarantee that every malformed or adversarial native document or resource fragment has bounded CPU usage. Review untrusted files in an appropriately isolated environment, and review exported XAML before compiling it in another application.

Recovery data is stored locally in the browser origin or native application data directory. It is not encrypted, authenticated, synchronized or backed up by DesignSpace. Avoid confidential production documents on shared browser profiles. Clearing site data can permanently remove recovery copies.

The `?diagnostics=1` browser option exposes the current document and source as a read-only JavaScript snapshot for testing; do not enable it while working with sensitive data. It provides no remote control endpoint and sends no telemetry.

Report security issues through the repository's private security advisory mechanism when available. Do not post private documents, credentials, personal data or exploit details in public issue reports.
