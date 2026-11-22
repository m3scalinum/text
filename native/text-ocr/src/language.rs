//! Which model set reads which page, and what a page is told when the set it asked
//! for is not there.
//!
//! A document that mixes languages needs more than one pair of weights. The caller
//! names one set per language and a default set; [`LanguageCatalogue`] decides, for a
//! requested language, which set reads the page, and records the fallback when the
//! requested tag has no set. The decision is a pure function of the catalogue and the
//! tag, so it is testable without loading a single model — which is what this
//! repository can do, since it ships none.
//!
//! The choice is **not** inferred from the text. Guessing a page's language and
//! re-recognising it until the score improves is a loop with no fixed point, and it
//! makes a reading depend on what the previous page happened to say. The page's
//! language is the caller's fact, so the caller supplies it.

use std::collections::BTreeMap;

use serde_json::{Value, json};

use crate::ffi::ToError;
use crate::models::{ModelIdentity, ModelSet};

/// Why a page was read with a set other than the one it asked for.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FallbackReason {
    /// The requested language has no model set in the catalogue.
    MissingLanguage,
}

impl FallbackReason {
    /// The stable name a report carries.
    pub fn name(self) -> &'static str {
        match self {
            Self::MissingLanguage => "missing_language",
        }
    }
}

/// A page that asked for a language this engine has no set for.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LanguageFallback {
    /// The language the page asked for, normalised.
    pub requested: String,
    /// The language whose set read it.
    pub used: String,
    /// Why the substitution happened.
    pub reason: FallbackReason,
}

impl LanguageFallback {
    /// One sentence naming the request, the set used and the reason.
    pub fn message(&self) -> String {
        format!(
            "the page asked for language '{}', which has no model set; it was read with the \
             default set '{}' ({})",
            self.requested,
            self.used,
            self.reason.name()
        )
    }
}

/// Which set reads one page, and what the result says about it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LanguageSelection {
    /// The language the page asked for; `None` when nothing was requested.
    pub requested: Option<String>,
    /// The language whose set actually read it.
    pub used: String,
    /// The substitution, when there was one. `None` means the request was honoured.
    pub fallback: Option<LanguageFallback>,
}

impl LanguageSelection {
    /// Whether the request was substituted.
    pub fn is_fallback(&self) -> bool {
        self.fallback.is_some()
    }

    /// The document a page carries as its `language` object.
    ///
    /// Always an object, even when nothing was asked for: a reader then never has to
    /// tell "no language recorded" from "the default set". `requested` is `null`
    /// when the page named none, `fallback` is a boolean, and `reason` and `message`
    /// are present only when it is true.
    pub fn to_json(&self) -> Value {
        let mut value = json!({
            "requested": self.requested,
            "used": self.used,
            "fallback": self.is_fallback(),
        });

        if let Some(fallback) = &self.fallback {
            value["reason"] = json!(fallback.reason.name());
            value["message"] = json!(fallback.message());
        }

        value
    }
}

/// The form a tag is compared in: trimmed and ASCII-lowercased.
///
/// Empty, and whitespace only, is not a tag: it reads as "no request", which is the
/// default set, not a fallback.
pub fn normalize_tag(tag: &str) -> Option<String> {
    let trimmed = tag.trim();

    if trimmed.is_empty() {
        return None;
    }

    Some(trimmed.to_ascii_lowercase())
}

/// One validated model set per language, and the one a page gets when it asks for
/// nothing or for a language that is not here.
///
/// The default tag is itself one of the sets: a catalogue whose default has no
/// weights could not honour its own promise, so [`LanguageCatalogue::new`] refuses
/// it.
#[derive(Clone, Debug)]
pub struct LanguageCatalogue {
    default_tag: String,
    sets: BTreeMap<String, ModelSet>,
}

impl LanguageCatalogue {
    /// Builds a catalogue, refusing an empty default, a repeated tag and a default
    /// that has no set.
    pub fn new(
        default_tag: &str,
        sets: impl IntoIterator<Item = (String, ModelSet)>,
    ) -> Result<Self, ToError> {
        let default_tag = normalize_tag(default_tag)
            .ok_or_else(|| ToError::invalid_argument("the default language is empty"))?;

        let mut by_tag = BTreeMap::new();

        for (tag, set) in sets {
            let normalized = normalize_tag(&tag).ok_or_else(|| {
                ToError::invalid_argument("a language tag in the model sets is empty")
            })?;

            if by_tag.insert(normalized.clone(), set).is_some() {
                return Err(ToError::invalid_argument(format!(
                    "language '{normalized}' has more than one model set"
                )));
            }
        }

        if !by_tag.contains_key(&default_tag) {
            return Err(ToError::invalid_argument(format!(
                "the default language '{default_tag}' has no model set"
            )));
        }

        Ok(Self {
            default_tag,
            sets: by_tag,
        })
    }

    /// The tag whose set reads a page that asks for nothing.
    pub fn default_tag(&self) -> &str {
        &self.default_tag
    }

    /// The set for a normalised tag, when the catalogue holds one.
    pub fn set(&self, tag: &str) -> Option<&ModelSet> {
        self.sets.get(tag)
    }

    /// The tags the catalogue holds, in sorted order.
    pub fn tags(&self) -> impl Iterator<Item = &str> {
        self.sets.keys().map(String::as_str)
    }

    /// The identity of the set a tag names, when there is one.
    pub fn identity(&self, tag: &str) -> Option<ModelIdentity> {
        self.sets.get(tag).map(ModelSet::identity)
    }

    /// Decides which set reads a page that asked for `requested`.
    ///
    /// A tag that is absent — or empty — is the default set and is not a fallback:
    /// nothing was asked for, so nothing was substituted. A tag that is present but
    /// has no set is a fallback, and it is always recorded as one.
    pub fn select(&self, requested: Option<&str>) -> LanguageSelection {
        match requested.and_then(normalize_tag) {
            None => LanguageSelection {
                requested: None,
                used: self.default_tag.clone(),
                fallback: None,
            },
            Some(tag) if self.sets.contains_key(&tag) => LanguageSelection {
                requested: Some(tag.clone()),
                used: tag,
                fallback: None,
            },
            Some(tag) => LanguageSelection {
                requested: Some(tag.clone()),
                used: self.default_tag.clone(),
                fallback: Some(LanguageFallback {
                    requested: tag,
                    used: self.default_tag.clone(),
                    reason: FallbackReason::MissingLanguage,
                }),
            },
        }
    }

    /// The document the result root carries: the default and the tags available.
    pub fn to_json(&self) -> Value {
        json!({
            "default": self.default_tag,
            "available": self.tags().collect::<Vec<_>>(),
        })
    }
}

/// What each page of a range asks for.
///
/// One entry per page of the range, in page order: entry 0 is the first rendered
/// page, whatever its index in the document. `None` means "no request" — the page is
/// read with the default set and no fallback is reported. The ABI carries it as a
/// JSON array of strings and nulls, because the list is as long as the range and a
/// C struct would be a fixed shape for a variable one.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct PageLanguages {
    entries: Vec<Option<String>>,
}

impl PageLanguages {
    /// A plan that asks for nothing, which is every page on the default set.
    pub fn none() -> Self {
        Self::default()
    }

    /// How many pages the plan speaks for.
    pub fn len(&self) -> usize {
        self.entries.len()
    }

    /// Whether the plan speaks for no page at all.
    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    /// The language page `index` asks for, `first_page` being entry 0.
    pub fn for_page(&self, index: u32, first_page: u32) -> Option<&str> {
        let offset = index.checked_sub(first_page)? as usize;

        self.entries
            .get(offset)
            .and_then(|entry| entry.as_deref())
            .filter(|tag| !tag.trim().is_empty())
    }

    /// Parses the ABI form: a JSON array of strings and nulls.
    ///
    /// An empty byte range is a plan that asks for nothing.
    pub fn parse(raw: &[u8]) -> Result<Self, ToError> {
        if raw.is_empty() {
            return Ok(Self::none());
        }

        let value: Value = serde_json::from_slice(raw).map_err(|error| {
            ToError::invalid_argument(format!("page languages are not valid JSON: {error}"))
        })?;

        let entries = value.as_array().ok_or_else(|| {
            ToError::invalid_argument("page languages must be a JSON array of strings and nulls")
        })?;

        let mut parsed = Vec::with_capacity(entries.len());

        for entry in entries {
            match entry {
                Value::Null => parsed.push(None),
                Value::String(tag) => parsed.push(Some(tag.clone())),
                _ => {
                    return Err(ToError::invalid_argument(
                        "page languages must be a JSON array of strings and nulls",
                    ));
                }
            }
        }

        Ok(Self { entries: parsed })
    }
}

/// Which set reads one page, with the identity of that set's weights.
#[derive(Clone, Debug)]
pub struct PageRoute {
    /// The page's index in the document.
    pub page_index: u32,
    /// The decision, including the fallback when there was one.
    pub selection: LanguageSelection,
    /// The identity of the weights that will read this page.
    pub identity: ModelIdentity,
}

/// The routing decision for a range, computed from the catalogue alone.
///
/// Nothing is loaded here: the identity a page carries is the identity of the set the
/// catalogue picked, which is the same value the loaded engine reports, since both are
/// `ModelSet::identity` over the same bytes. That is what lets a test with no weights
/// prove that page 1 and page 2 are read by two different models.
///
/// A plan that names a different number of pages than the range is refused: a
/// misaligned plan would read some page with a language that belongs to another, and
/// silence is the one thing this feature must not do.
pub fn route_pages(
    catalogue: &LanguageCatalogue,
    indices: &[u32],
    first_page: u32,
    languages: &PageLanguages,
) -> Result<Vec<PageRoute>, ToError> {
    if !languages.is_empty() && languages.len() != indices.len() {
        return Err(ToError::invalid_argument(format!(
            "{} language entries for {} page(s): a plan names one language, or null, per page",
            languages.len(),
            indices.len()
        )));
    }

    let mut routes = Vec::with_capacity(indices.len());

    for &index in indices {
        let selection = catalogue.select(languages.for_page(index, first_page));
        let identity = catalogue.identity(&selection.used).ok_or_else(|| {
            ToError::invalid_argument(format!("language '{}' has no model set", selection.used))
        })?;

        routes.push(PageRoute {
            page_index: index,
            selection,
            identity,
        });
    }

    Ok(routes)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::ModelKind;

    /// A model set whose bytes are a well-formed `RTEN` v2 container and differ from
    /// `seed`, so two sets have two identities without a real model behind them.
    ///
    /// The container check is all `ModelSet::from_bytes` does; the loader would
    /// refuse these zeros, and no engine is built here.
    fn model_set(seed: u8) -> ModelSet {
        let detection = container(seed, 1024, 8192);
        let recognition = container(seed.wrapping_add(1), 2048, 8192);

        ModelSet::from_bytes(&detection, &recognition).expect("both files are containers")
    }

    fn container(seed: u8, graph_len: usize, total_len: usize) -> Vec<u8> {
        let model_offset = 32u64;
        let mut bytes = vec![seed; total_len];

        bytes[..4].copy_from_slice(b"RTEN");
        bytes[4..8].copy_from_slice(&2u32.to_le_bytes());
        bytes[8..16].copy_from_slice(&model_offset.to_le_bytes());
        bytes[16..24].copy_from_slice(&(graph_len as u64).to_le_bytes());
        bytes[24..32].copy_from_slice(&(model_offset + graph_len as u64).to_le_bytes());

        bytes
    }

    fn catalogue() -> LanguageCatalogue {
        LanguageCatalogue::new(
            "fr",
            vec![
                ("fr".to_string(), model_set(1)),
                ("en".to_string(), model_set(9)),
            ],
        )
        .expect("the default has a set")
    }

    /// A plan, written the way the ABI carries one: a JSON array of tags and nulls.
    fn plan(entries: &[Option<&str>]) -> PageLanguages {
        let items = entries
            .iter()
            .map(|entry| match entry {
                Some(tag) => format!("\"{tag}\""),
                None => "null".to_string(),
            })
            .collect::<Vec<_>>()
            .join(",");

        PageLanguages::parse(format!("[{items}]").as_bytes()).expect("a plan")
    }

    #[test]
    fn a_page_that_asks_for_nothing_is_the_default_and_not_a_fallback() {
        let catalogue = catalogue();
        let selection = catalogue.select(None);

        assert_eq!(selection.requested, None);
        assert_eq!(selection.used, "fr");
        assert!(!selection.is_fallback());

        let document = selection.to_json();
        assert_eq!(document["requested"], Value::Null);
        assert_eq!(document["used"], "fr");
        assert_eq!(document["fallback"], false);
        assert!(document.get("reason").is_none());
    }

    #[test]
    fn a_page_that_asks_for_a_set_here_gets_it_without_a_fallback() {
        let selection = catalogue().select(Some("EN"));

        assert_eq!(selection.requested.as_deref(), Some("en"));
        assert_eq!(selection.used, "en");
        assert!(!selection.is_fallback());
    }

    #[test]
    fn a_tag_with_no_set_falls_back_to_the_default_and_says_so() {
        let selection = catalogue().select(Some("de"));

        assert!(selection.is_fallback());
        assert_eq!(selection.requested.as_deref(), Some("de"));
        assert_eq!(selection.used, "fr");

        let document = selection.to_json();
        assert_eq!(document["requested"], "de");
        assert_eq!(document["used"], "fr");
        assert_eq!(document["fallback"], true);
        assert_eq!(document["reason"], "missing_language");

        let message = document["message"].as_str().expect("a message");
        assert!(message.contains("'de'"), "{message}");
        assert!(message.contains("'fr'"), "{message}");
        assert!(message.contains("missing_language"), "{message}");
    }

    #[test]
    fn a_blank_tag_is_a_request_for_nothing_and_not_for_a_language() {
        let catalogue = catalogue();

        for tag in ["", "   ", "\t"] {
            let selection = catalogue.select(Some(tag));
            assert_eq!(selection.requested, None, "{tag:?}");
            assert_eq!(selection.used, "fr");
            assert!(!selection.is_fallback(), "{tag:?}");
        }
    }

    #[test]
    fn the_page_index_picks_the_entry_and_never_wraps() {
        let pages = plan(&[Some("fr"), Some("en"), None]);

        // Entries are aligned to the range, so the first page of a range starting at
        // 4 is entry 0.
        assert_eq!(pages.for_page(4, 4), Some("fr"));
        assert_eq!(pages.for_page(5, 4), Some("en"));
        assert_eq!(pages.for_page(6, 4), None);
        assert_eq!(pages.for_page(7, 4), None);
        // A page before the range is not entry zero by underflow.
        assert_eq!(pages.for_page(3, 4), None);
    }

    #[test]
    fn the_page_plan_parses_the_json_array_the_abi_carries() {
        let pages = PageLanguages::parse(br#"["fr", null, "en"]"#).expect("a plan");

        assert_eq!(pages.len(), 3);
        assert_eq!(pages.for_page(0, 0), Some("fr"));
        assert_eq!(pages.for_page(1, 0), None);
        assert_eq!(pages.for_page(2, 0), Some("en"));

        assert_eq!(
            PageLanguages::parse(b"").expect("an empty plan"),
            PageLanguages::none()
        );
        assert!(PageLanguages::parse(b"[\"fr\", 3]").is_err());
        assert!(PageLanguages::parse(b"{\"fr\":0}").is_err());
        assert!(PageLanguages::parse(b"not json").is_err());
    }

    #[test]
    fn a_catalogue_refuses_a_default_with_no_set_and_a_repeated_language() {
        let error = LanguageCatalogue::new("de", vec![("fr".to_string(), model_set(1))])
            .expect_err("the default has no set");
        assert_eq!(error.code, crate::ffi::TO_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("'de'"), "{}", error.json);

        let error = LanguageCatalogue::new(
            "fr",
            vec![
                ("fr".to_string(), model_set(1)),
                ("FR".to_string(), model_set(2)),
            ],
        )
        .expect_err("fr appears twice, once lowercased");
        assert!(
            error.json.contains("more than one model set"),
            "{}",
            error.json
        );

        let error = LanguageCatalogue::new("", vec![("fr".to_string(), model_set(1))])
            .expect_err("an empty default is not a language");
        assert!(
            error.json.contains("default language is empty"),
            "{}",
            error.json
        );
    }

    #[test]
    fn the_catalogue_reports_its_default_and_its_available_tags() {
        let document = catalogue().to_json();

        assert_eq!(document["default"], "fr");
        assert_eq!(
            document["available"],
            json!(["en", "fr"]),
            "the tags are sorted and normalised"
        );
    }

    #[test]
    fn two_languages_name_two_different_model_sets() {
        let catalogue = catalogue();

        let fr = catalogue.identity("fr").expect("fr is there");
        let en = catalogue.identity("en").expect("en is there");

        assert_ne!(
            fr.to_json()["set"],
            en.to_json()["set"],
            "the two sets are different weights, so a page read by either is identifiable"
        );

        // The identity a page reports is the selected set's, so a report names the
        // weights of each page rather than one pair for the whole document.
        let used = catalogue.select(Some("en")).used;
        let identity = catalogue.identity(&used).expect("the used tag is a set");
        assert_eq!(identity.to_json()["set"], en.to_json()["set"]);
    }

    #[test]
    fn the_model_kind_is_not_confused_by_two_sets_of_the_same_length() {
        // A guard on the fixture itself: both sets hold two containers of the same
        // length, so only their bytes tell them apart — which is the point.
        let first = model_set(1);
        let second = model_set(9);

        assert_eq!(
            first.detection().fingerprint.length,
            second.detection().fingerprint.length
        );
        assert_ne!(
            first.detection().fingerprint.sha256_hex(),
            second.detection().fingerprint.sha256_hex()
        );
        assert_eq!(first.detection().fingerprint.kind, ModelKind::Detection);
    }

    #[test]
    fn page_one_and_page_two_route_to_two_different_model_sets() {
        let catalogue = catalogue();
        let languages = plan(&[Some("fr"), Some("en")]);

        let routes = route_pages(&catalogue, &[0, 1], 0, &languages).expect("an aligned plan");

        assert_eq!(routes.len(), 2);
        assert_eq!(routes[0].page_index, 0);
        assert_eq!(routes[0].selection.used, "fr");
        assert_eq!(routes[1].page_index, 1);
        assert_eq!(routes[1].selection.used, "en");
        assert!(!routes[0].selection.is_fallback());
        assert!(!routes[1].selection.is_fallback());

        // The identities the two pages will report are the two sets' — different
        // weights, so a page really changes model rather than relabelling the same one.
        assert_eq!(
            routes[0].identity.to_json()["set"],
            catalogue.identity("fr").expect("fr").to_json()["set"]
        );
        assert_eq!(
            routes[1].identity.to_json()["set"],
            catalogue.identity("en").expect("en").to_json()["set"]
        );
        assert_ne!(
            routes[0].identity.to_json()["set"],
            routes[1].identity.to_json()["set"]
        );
    }

    #[test]
    fn a_page_whose_language_has_no_set_routes_to_the_default_and_reports_the_fallback() {
        let catalogue = catalogue();
        let languages = plan(&[Some("de")]);

        let routes = route_pages(&catalogue, &[0], 0, &languages).expect("an aligned plan");

        assert_eq!(routes.len(), 1);
        assert_eq!(routes[0].selection.used, "fr");
        assert!(routes[0].selection.is_fallback());
        assert_eq!(
            routes[0].identity.to_json()["set"],
            catalogue.identity("fr").expect("fr").to_json()["set"],
            "the fallback reads with the default set, never a third one"
        );

        let document = routes[0].selection.to_json();
        assert_eq!(document["requested"], "de");
        assert_eq!(document["used"], "fr");
        assert_eq!(document["reason"], "missing_language");
    }

    #[test]
    fn the_first_page_of_a_range_takes_the_first_entry_of_the_plan() {
        let catalogue = catalogue();
        let languages = plan(&[Some("en"), Some("fr")]);

        let routes = route_pages(&catalogue, &[4, 5], 4, &languages).expect("an aligned plan");

        assert_eq!(routes[0].page_index, 4);
        assert_eq!(routes[0].selection.used, "en");
        assert_eq!(routes[1].page_index, 5);
        assert_eq!(routes[1].selection.used, "fr");
    }

    #[test]
    fn a_plan_that_asks_for_nothing_reads_every_page_with_the_default() {
        let catalogue = catalogue();
        let routes = route_pages(&catalogue, &[0, 1, 2], 0, &PageLanguages::none())
            .expect("an empty plan is every page on the default");

        assert_eq!(routes.len(), 3);
        for route in &routes {
            assert_eq!(route.selection.used, "fr");
            assert_eq!(route.selection.requested, None);
            assert!(!route.selection.is_fallback());
        }

        let same = routes[0].identity.to_json()["set"].clone();
        assert!(
            routes
                .iter()
                .all(|route| route.identity.to_json()["set"] == same),
            "one set for the whole range when nothing is asked for"
        );
    }

    #[test]
    fn a_plan_for_another_number_of_pages_is_refused_rather_than_shifted() {
        let catalogue = catalogue();
        let languages = plan(&[Some("fr"), Some("en"), Some("fr")]);

        let error = route_pages(&catalogue, &[0, 1], 0, &languages)
            .expect_err("three entries cannot describe two pages");

        assert_eq!(error.code, crate::ffi::TO_ERR_INVALID_ARGUMENT);
        assert!(
            error.json.contains("3 language entries for 2 page(s)"),
            "{}",
            error.json
        );
    }
}
