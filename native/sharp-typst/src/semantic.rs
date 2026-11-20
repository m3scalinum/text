//! Markdown and plain-text export, derived from the semantic HTML tree.
//!
//! The paged document is deliberately not used: layout has already destroyed the
//! structure (a heading is a bold run, a list item is a bullet glyph). See
//! `docs/specifications/06-markdown-and-text-export.md`.

use typst_html::{HtmlAttr, HtmlDocument, HtmlElement, HtmlNode, attr};

/// Renders the document as GitHub-flavoured Markdown.
pub fn markdown(document: &HtmlDocument) -> String {
    let mut writer = Markdown::default();
    writer.nodes(&document.root().children);
    writer.finish()
}

/// Renders the document as plain text, wrapped at 100 columns.
pub fn text(document: &HtmlDocument) -> String {
    let mut writer = Plain::default();
    writer.nodes(&document.root().children);
    writer.finish()
}

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

fn tag_of(element: &HtmlElement) -> String {
    element.tag.resolve().as_str().to_string()
}

fn attr_of(element: &HtmlElement, attribute: HtmlAttr) -> Option<&str> {
    element.attrs.get(attribute).map(|value| value.as_str())
}

/// Concatenates every text node below `node`, without any markup.
fn plain_text(node: &HtmlNode, out: &mut String) {
    match node {
        HtmlNode::Text(text, _) => out.push_str(text),
        HtmlNode::Element(element) => {
            for child in &element.children {
                plain_text(child, out);
            }
        }
        HtmlNode::Frame(_) | HtmlNode::Tag(_) => {}
    }
}

fn text_of(element: &HtmlElement) -> String {
    let mut out = String::new();
    for child in &element.children {
        plain_text(child, &mut out);
    }
    out
}

/// Renders an element's children through the plain-text writer, so links and images keep
/// their target instead of collapsing to their label.
fn inline_text(element: &HtmlElement) -> String {
    let mut inner = Plain::default();
    inner.nodes(&element.children);
    inner.out
}

fn is_list(element: &HtmlElement) -> bool {
    matches!(tag_of(element).as_str(), "ul" | "ol")
}

/// Collects table rows, flattening `thead`/`tbody`/`tfoot`.
fn collect_rows(element: &HtmlElement, rows: &mut Vec<Vec<String>>) {
    for child in &element.children {
        let HtmlNode::Element(child) = child else {
            continue;
        };

        match tag_of(child).as_str() {
            "tr" => {
                let mut cells = Vec::new();
                for cell in &child.children {
                    if let HtmlNode::Element(cell) = cell
                        && matches!(tag_of(cell).as_str(), "td" | "th")
                    {
                        cells.push(text_of(cell).trim().to_string());
                    }
                }
                rows.push(cells);
            }
            "thead" | "tbody" | "tfoot" => collect_rows(child, rows),
            _ => {}
        }
    }
}

// ---------------------------------------------------------------------------
// Markdown
// ---------------------------------------------------------------------------

#[derive(Default)]
struct Markdown {
    out: String,
    list_depth: usize,
}

impl Markdown {
    fn finish(mut self) -> String {
        while self.out.ends_with('\n') {
            self.out.pop();
        }
        self.out.push('\n');
        self.out
    }

    fn blank(&mut self) {
        if self.out.is_empty() {
            return;
        }
        if !self.out.ends_with('\n') {
            self.out.push('\n');
        }
        if !self.out.ends_with("\n\n") {
            self.out.push('\n');
        }
    }

    fn nodes(&mut self, nodes: &[HtmlNode]) {
        for node in nodes {
            self.node(node);
        }
    }

    fn node(&mut self, node: &HtmlNode) {
        match node {
            HtmlNode::Text(text, _) => self.out.push_str(&escape(text)),
            HtmlNode::Element(element) => self.element(element),
            HtmlNode::Frame(_) => {
                self.blank();
                self.out.push_str("<!-- typst frame -->\n\n");
            }
            HtmlNode::Tag(_) => {}
        }
    }

    fn element(&mut self, element: &HtmlElement) {
        match tag_of(element).as_str() {
            "h1" | "h2" | "h3" | "h4" | "h5" | "h6" => {
                let level = tag_of(element)[1..].parse::<usize>().unwrap_or(1);
                self.blank();
                self.out.push_str(&"#".repeat(level));
                self.out.push(' ');
                self.nodes(&element.children);
                self.out.push_str("\n\n");
            }
            "p" => {
                self.nodes(&element.children);
                self.out.push_str("\n\n");
            }
            "strong" | "b" => self.wrap("**", element),
            "em" | "i" => self.wrap("*", element),
            "code" => {
                self.out.push('`');
                self.out.push_str(&text_of(element));
                self.out.push('`');
            }
            "pre" => self.code_block(element),
            "ul" | "ol" => self.list(element),
            "blockquote" => self.quote(element),
            "table" => self.table(element),
            "a" => self.link(element),
            "img" => self.image(element),
            "hr" => {
                self.blank();
                self.out.push_str("---\n\n");
            }
            "br" => self.out.push_str("  \n"),
            "figcaption" => {
                self.out.push('*');
                self.nodes(&element.children);
                self.out.push_str("*\n\n");
            }
            "math" => {
                self.out.push('$');
                self.out.push_str(&text_of(element));
                self.out.push('$');
            }
            _ => self.nodes(&element.children),
        }
    }

    fn wrap(&mut self, marker: &str, element: &HtmlElement) {
        self.out.push_str(marker);
        self.nodes(&element.children);
        self.out.push_str(marker);
    }

    fn code_block(&mut self, element: &HtmlElement) {
        let mut language_name = String::new();
        let mut code = String::new();

        for child in &element.children {
            if let HtmlNode::Element(child) = child
                && tag_of(child) == "code"
            {
                // Typst emits the language as `data-lang`; `class="language-…"` is the
                // conventional fallback for hand-written HTML.
                if let Some(language) = attr_of(child, HtmlAttr::constant("data-lang")) {
                    language_name.push_str(language);
                } else if let Some(class) = attr_of(child, attr::class)
                    && let Some(name) = class.strip_prefix("language-")
                {
                    language_name.push_str(name);
                }
                code.push_str(&text_of(child));
            }
        }

        if code.is_empty() {
            code = text_of(element);
        }

        self.blank();
        self.out.push_str("```");
        self.out.push_str(&language_name);
        self.out.push('\n');
        self.out.push_str(code.trim_end_matches('\n'));
        self.out.push_str("\n```\n\n");
    }

    fn list(&mut self, element: &HtmlElement) {
        let ordered = tag_of(element) == "ol";
        self.blank();
        self.list_depth += 1;

        let mut index = 0;
        for child in &element.children {
            let HtmlNode::Element(item) = child else {
                continue;
            };
            if tag_of(item) != "li" {
                continue;
            }

            index += 1;
            self.out.push_str(&"  ".repeat(self.list_depth - 1));
            if ordered {
                self.out.push_str(&format!("{index}. "));
            } else {
                self.out.push_str("- ");
            }

            for node in &item.children {
                if let HtmlNode::Element(child) = node
                    && is_list(child)
                {
                    continue;
                }
                self.node(node);
            }

            if !self.out.ends_with('\n') {
                self.out.push('\n');
            }

            for node in &item.children {
                if let HtmlNode::Element(child) = node
                    && is_list(child)
                {
                    self.list(child);
                }
            }
        }

        self.list_depth -= 1;
        self.out.push('\n');
    }

    fn quote(&mut self, element: &HtmlElement) {
        let mut inner = Markdown::default();
        inner.nodes(&element.children);
        let body = inner.finish();

        self.blank();
        for line in body.lines() {
            self.out.push_str("> ");
            self.out.push_str(line);
            self.out.push('\n');
        }
        self.out.push('\n');
    }

    fn table(&mut self, element: &HtmlElement) {
        let mut rows = Vec::new();
        collect_rows(element, &mut rows);
        if rows.is_empty() {
            return;
        }

        self.blank();
        let columns = rows.iter().map(Vec::len).max().unwrap_or(0);

        for (index, row) in rows.iter().enumerate() {
            self.out.push('|');
            for column in 0..columns {
                let cell = row.get(column).map(String::as_str).unwrap_or_default();
                self.out.push(' ');
                self.out.push_str(&cell.replace('|', "\\|"));
                self.out.push_str(" |");
            }
            self.out.push('\n');

            if index == 0 {
                self.out.push('|');
                for _ in 0..columns {
                    self.out.push_str(" --- |");
                }
                self.out.push('\n');
            }
        }

        self.out.push('\n');
    }

    fn link(&mut self, element: &HtmlElement) {
        let href = attr_of(element, attr::href).unwrap_or_default();
        let label = text_of(element);

        if label.trim().is_empty() || label.trim() == href {
            self.out.push('<');
            self.out.push_str(href);
            self.out.push('>');
            return;
        }

        self.out.push('[');
        self.nodes(&element.children);
        self.out.push_str("](");
        self.out.push_str(href);
        self.out.push(')');
    }

    fn image(&mut self, element: &HtmlElement) {
        let src = attr_of(element, attr::src).unwrap_or_default();
        let alt = attr_of(element, attr::alt).unwrap_or_default();

        self.out.push_str("![");
        self.out.push_str(alt);
        self.out.push_str("](");
        self.out.push_str(src);
        self.out.push(')');
    }
}

fn escape(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    for (index, ch) in text.chars().enumerate() {
        match ch {
            '\\' | '`' | '*' | '_' | '[' | ']' | '<' | '>' => {
                out.push('\\');
                out.push(ch);
            }
            '#' if index == 0 => {
                out.push('\\');
                out.push('#');
            }
            _ => out.push(ch),
        }
    }
    out
}

// ---------------------------------------------------------------------------
// Plain text
// ---------------------------------------------------------------------------

#[derive(Default)]
struct Plain {
    out: String,
    list_depth: usize,
}

impl Plain {
    fn finish(mut self) -> String {
        while self.out.ends_with('\n') {
            self.out.pop();
        }
        self.out.push('\n');
        self.out
    }

    fn blank(&mut self) {
        if self.out.is_empty() {
            return;
        }
        if !self.out.ends_with('\n') {
            self.out.push('\n');
        }
        if !self.out.ends_with("\n\n") {
            self.out.push('\n');
        }
    }

    fn nodes(&mut self, nodes: &[HtmlNode]) {
        for node in nodes {
            self.node(node);
        }
    }

    fn node(&mut self, node: &HtmlNode) {
        match node {
            HtmlNode::Text(text, _) => self.out.push_str(text),
            HtmlNode::Element(element) => self.element(element),
            HtmlNode::Frame(_) | HtmlNode::Tag(_) => {}
        }
    }

    fn element(&mut self, element: &HtmlElement) {
        match tag_of(element).as_str() {
            "h1" | "h2" => {
                let body = inline_text(element);
                let underline = if tag_of(element) == "h1" { '=' } else { '-' };
                self.blank();
                self.out.push_str(body.trim());
                self.out.push('\n');
                self.out
                    .push_str(&underline.to_string().repeat(body.trim().chars().count()));
                self.out.push_str("\n\n");
            }
            "h3" | "h4" | "h5" | "h6" => {
                self.blank();
                self.out.push_str(inline_text(element).trim());
                self.out.push_str("\n\n");
            }
            "p" => {
                let body = inline_text(element);
                self.out.push_str(&wrap(&body, 100));
                self.out.push_str("\n\n");
            }
            "pre" => {
                self.blank();
                for line in text_of(element).trim_end().lines() {
                    self.out.push_str("    ");
                    self.out.push_str(line);
                    self.out.push('\n');
                }
                self.out.push('\n');
            }
            "ul" | "ol" => self.list(element),
            "blockquote" => {
                let body = inline_text(element);
                self.blank();
                for line in wrap(&body, 96).lines() {
                    self.out.push_str("> ");
                    self.out.push_str(line);
                    self.out.push('\n');
                }
                self.out.push('\n');
            }
            "table" => self.table(element),
            "a" => {
                let href = attr_of(element, attr::href).unwrap_or_default();
                let label = text_of(element);
                if label.trim().is_empty() || label.trim() == href {
                    self.out.push_str(href);
                } else {
                    self.out.push_str(label.trim());
                    self.out.push_str(" (");
                    self.out.push_str(href);
                    self.out.push(')');
                }
            }
            "img" => {
                let alt = attr_of(element, attr::alt).unwrap_or_default();
                self.out.push_str("[image: ");
                self.out.push_str(alt);
                self.out.push(']');
            }
            "hr" => {
                self.blank();
                self.out.push_str(&"─".repeat(40));
                self.out.push_str("\n\n");
            }
            "br" => self.out.push('\n'),
            "math" => self.out.push_str(&text_of(element)),
            _ => self.nodes(&element.children),
        }
    }

    fn list(&mut self, element: &HtmlElement) {
        let ordered = tag_of(element) == "ol";
        self.blank();
        self.list_depth += 1;

        let mut index = 0;
        for child in &element.children {
            let HtmlNode::Element(item) = child else {
                continue;
            };
            if tag_of(item) != "li" {
                continue;
            }

            index += 1;
            self.out.push_str(&"  ".repeat(self.list_depth - 1));
            if ordered {
                self.out.push_str(&format!("{index}. "));
            } else {
                self.out.push_str("- ");
            }

            let mut inner = Plain::default();
            for node in &item.children {
                if let HtmlNode::Element(child) = node
                    && is_list(child)
                {
                    continue;
                }
                inner.node(node);
            }
            self.out.push_str(inner.out.trim());
            self.out.push('\n');

            for node in &item.children {
                if let HtmlNode::Element(child) = node
                    && is_list(child)
                {
                    self.list(child);
                }
            }
        }

        self.list_depth -= 1;
        self.out.push('\n');
    }

    fn table(&mut self, element: &HtmlElement) {
        let mut rows = Vec::new();
        collect_rows(element, &mut rows);
        if rows.is_empty() {
            return;
        }

        self.blank();
        let columns = rows.iter().map(Vec::len).max().unwrap_or(0);

        for (index, row) in rows.iter().enumerate() {
            let line_start = self.out.len();
            for column in 0..columns {
                if column > 0 {
                    self.out.push_str(" | ");
                }
                self.out
                    .push_str(row.get(column).map(String::as_str).unwrap_or_default());
            }
            let width = self.out.len() - line_start;
            self.out.push('\n');

            if index == 0 {
                self.out.push_str(&"-".repeat(width.max(3)));
                self.out.push('\n');
            }
        }

        self.out.push('\n');
    }
}

/// Greedy word wrap that never breaks inside a word longer than `width`.
fn wrap(text: &str, width: usize) -> String {
    let mut out = String::with_capacity(text.len());
    let mut line = 0usize;

    for word in text.split_whitespace() {
        let length = word.chars().count();
        if line > 0 && line + 1 + length > width {
            out.push('\n');
            line = 0;
        } else if line > 0 {
            out.push(' ');
            line += 1;
        }

        out.push_str(word);
        line += length;
    }

    out
}
