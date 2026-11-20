//! Introspection: what a compiled document says about itself.
//!
//! The host gets the outline, the labels and the metadata entries as JSON, so a table of contents
//! or a cross-reference table can be built without re-parsing the source.

use std::num::NonZeroUsize;

use serde::Serialize;
use typst::foundations::{Element, Selector, Smart, Value};
use typst::introspection::MetadataElem;
use typst::model::HeadingElem;

use crate::Document;
use crate::ffi::{TN_ERR_EXPORT, TnError};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Evaluation {
    page_count: usize,
    outline: Vec<OutlineEntry>,
    labels: Vec<LabelEntry>,
    metadata: Vec<MetadataEntry>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct OutlineEntry {
    level: usize,
    title: String,
    page: usize,
    x: f64,
    y: f64,
    label: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct LabelEntry {
    name: String,
    page: usize,
    x: f64,
    y: f64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct MetadataEntry {
    value: Value,
    page: usize,
}

/// Evaluates the document's introspection data.
pub fn evaluate(document: &Document) -> Result<Vec<u8>, TnError> {
    let Document::Paged(doc) = document else {
        return Err(TnError::invalid_argument(
            "evaluation requires a paged document; compile with document = \"paged\"",
        ));
    };

    let elements = doc.introspector().elements();

    let mut outline = Vec::new();
    for content in elements.query(&Selector::Elem(Element::of::<HeadingElem>(), None)) {
        let Some(heading) = content.to_packed::<HeadingElem>() else {
            continue;
        };
        let Some(position) = content.location().and_then(|l| elements.position(l)) else {
            continue;
        };

        // `level: auto` means "offset + depth", the same rule the layout applies. A packed element
        // has already been realized, so the level is normally `Custom`.
        let offset = (*heading.offset.as_option()).unwrap_or(0);
        let depth = (*heading.depth.as_option()).unwrap_or(NonZeroUsize::MIN);
        let level = match heading.level.as_option() {
            Some(Smart::Custom(level)) => level.get(),
            _ => offset + depth.get(),
        };

        outline.push(OutlineEntry {
            level,
            // `plain_text()` on the whole element would prepend the supplement ("Section") and the
            // numbering; the title is the body alone.
            title: heading.body.plain_text().to_string(),
            page: position.page.get(),
            x: position.point.x.to_pt(),
            y: position.point.y.to_pt(),
            label: content.label().map(|label| label.resolve().to_string()),
        });
    }

    let mut labels = Vec::new();
    for content in elements.query_labelled() {
        let Some(label) = content.label() else {
            continue;
        };
        let Some(position) = content.location().and_then(|l| elements.position(l)) else {
            continue;
        };

        labels.push(LabelEntry {
            name: label.resolve().to_string(),
            page: position.page.get(),
            x: position.point.x.to_pt(),
            y: position.point.y.to_pt(),
        });
    }

    let mut metadata = Vec::new();
    for content in elements.query(&Selector::Elem(Element::of::<MetadataElem>(), None)) {
        let Some(entry) = content.to_packed::<MetadataElem>() else {
            continue;
        };

        let page = content
            .location()
            .and_then(|l| elements.position(l))
            .map_or(0, |position| position.page.get());

        metadata.push(MetadataEntry {
            value: entry.value.clone(),
            page,
        });
    }

    serde_json::to_vec(&Evaluation {
        page_count: doc.pages().len(),
        outline,
        labels,
        metadata,
    })
    .map_err(|e| TnError::new(TN_ERR_EXPORT, format!("evaluation failed: {e}")))
}
