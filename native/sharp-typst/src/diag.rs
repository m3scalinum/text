//! Conversion of Typst diagnostics into the JSON payload described in the spec.

use typst::diag::{Severity, SourceDiagnostic};
use typst::syntax::DiagSpan;

use crate::world::NetWorld;

/// Serialises a diagnostic slice for a world.
pub fn to_json(world: &NetWorld, diagnostics: &[SourceDiagnostic], code: &str) -> String {
    build(diagnostics, code, |span| world.locate(span))
}

/// Serialises diagnostics that have no world to resolve spans against (export failures).
pub fn to_json_empty(diagnostics: &[SourceDiagnostic], code: &str) -> String {
    build(diagnostics, code, |_| None)
}

fn build(
    diagnostics: &[SourceDiagnostic],
    code: &str,
    locate: impl Fn(DiagSpan) -> Option<(String, usize, usize, usize, usize, bool, String)>,
) -> String {
    let items: Vec<serde_json::Value> = diagnostics
        .iter()
        .map(|d| {
            let location = locate(d.span);
            let (file, line, column, end_line, end_column, in_source, source_line) = match location
            {
                Some((f, l, c, el, ec, s, sl)) => {
                    (Some(f), Some(l), Some(c), Some(el), Some(ec), s, Some(sl))
                }
                None => (None, None, None, None, None, false, None),
            };

            let hints: Vec<String> = d.hints.iter().map(|h| h.v.to_string()).collect();
            let trace: Vec<String> = d.trace.iter().map(|t| t.v.to_string()).collect();

            serde_json::json!({
                "severity": severity_name(d.severity),
                "message": d.message.to_string(),
                "hints": hints,
                "trace": trace,
                "file": file,
                "line": line,
                "column": column,
                "endLine": end_line,
                "endColumn": end_column,
                "isInSource": in_source,
                "sourceLine": source_line,
            })
        })
        .collect();

    serde_json::json!({
        "code": code,
        "message": summary(&items),
        "diagnostics": items,
    })
    .to_string()
}

fn summary(items: &[serde_json::Value]) -> String {
    let errors = items.iter().filter(|d| d["severity"] == "error").count();
    let warnings = items.iter().filter(|d| d["severity"] == "warning").count();

    let first = items.first().map(|d| {
        let message = d["message"].as_str().unwrap_or_default();
        let file = d["file"].as_str().unwrap_or("<?>");
        let line = d["line"].as_i64().unwrap_or(0);
        let column = d["column"].as_i64().unwrap_or(0);
        format!("{message} at {file}:{line}:{column}")
    });

    match (errors, warnings, first) {
        (0, 0, _) => "compilation failed".to_string(),
        (e, 0, Some(f)) => format!("{e} error(s), first: {f}"),
        (0, w, Some(f)) => format!("{w} warning(s), first: {f}"),
        (e, w, Some(f)) => format!("{e} error(s), {w} warning(s), first: {f}"),
        _ => "compilation failed".to_string(),
    }
}

fn severity_name(severity: Severity) -> &'static str {
    match severity {
        Severity::Error => "error",
        Severity::Warning => "warning",
    }
}
