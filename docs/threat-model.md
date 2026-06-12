# Threat Model

The primary viewer threat is analyzing an untrusted repository and then opening the generated HTML report.
Repository-controlled file paths, type and member names, dependency labels, and smell messages can flow into the analysis JSON embedded in an inline script.
An attacker may include HTML or `</script>` sequences in those values to try to escape the data script and execute code in the viewer's local origin.
Unilyze mitigates this by using `System.Text.Json`'s default encoder for normal analysis output and by rewriting every case-insensitive `</script` sequence to `<\/script` before embedding analysis or diff JSON.
Users should still treat generated reports as untrusted artifacts, avoid weakening browser security controls, and update to the latest unilyze release before opening reports from unknown repositories.
