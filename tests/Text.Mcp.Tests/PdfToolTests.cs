using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.Pdf;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The three PDF tools that take a document rather than produce one: <c>sign_document</c>,
/// <c>merge_documents</c> and <c>extract_text</c>.
/// </summary>
/// <remarks>
/// A PDF arrives as base64 or as a path the host allowed. Every test here drives the real server over
/// a real frame, and the documents it signs, merges and reads are produced by the same server's
/// <c>render_document</c>, so nothing is a fixture that could drift from what the suite writes.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PdfToolTests
{
    private const string SkipReason =
        "set SHARPTYPST_NATIVE_PATH to native/sharp-typst/target/release/sharp_typst.dll "
        + "(or libsharp_typst.so) to run the render tests";

    private const string Password = "m3scalinum-test";

    private static bool NativeAvailable
        => Environment.GetEnvironmentVariable("SHARPTYPST_NATIVE_PATH") is { Length: > 0 } path
            && File.Exists(path);

    [Fact]
    public async Task Sign_document_returns_a_signed_pdf()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var pdf = await RenderAsync(1, "= First document");
        var certificate = Certificate(Password);

        var arguments = string.Concat(
            "{\"pdf\":\"", Convert.ToBase64String(pdf),
            "\",\"certificate\":\"", Convert.ToBase64String(certificate),
            "\",\"password\":\"", Password,
            "\",\"reason\":\"Contract signature\",\"location\":\"Paris\"}");

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(2, McpToolNames.SignDocument, arguments)));

        var signed = McpTestClient.RenderedPdf(Assert.Single(responses), out var summary);

        Assert.Equal("%PDF"u8.ToArray(), signed[..4]);

        var verification = PdfSignatureVerifier.Verify(signed);
        Assert.True(verification.IsSigned, "the returned file carries no signature");
        Assert.True(verification.AllIntact, "the signature does not verify over the bytes it covers");

        using var metadata = JsonDocument.Parse(summary.GetProperty("text").GetString()!);
        Assert.Equal(1, metadata.RootElement.GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public async Task Merge_documents_returns_one_pdf_with_both_pages()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var first = await RenderAsync(3, "= First document");
        var second = await RenderAsync(4, "= Second document");

        var arguments = string.Concat(
            "{\"documents\":[\"", Convert.ToBase64String(first),
            "\",\"", Convert.ToBase64String(second), "\"]}");

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(5, McpToolNames.MergeDocuments, arguments)));

        var merged = McpTestClient.RenderedPdf(Assert.Single(responses), out var summary);

        Assert.Equal("%PDF"u8.ToArray(), merged[..4]);

        using var metadata = JsonDocument.Parse(summary.GetProperty("text").GetString()!);
        Assert.Equal(2, metadata.RootElement.GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public async Task Merge_documents_keeps_the_order_the_call_gives()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var first = await RenderAsync(6, "= Alpha marker");
        var second = await RenderAsync(7, "= Beta marker");

        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(McpTestClient.ToolCall(
            8,
            McpToolNames.MergeDocuments,
            string.Concat(
                "{\"documents\":[\"", Convert.ToBase64String(first),
                "\",\"", Convert.ToBase64String(second), "\"]}"))));

        var merged = McpTestClient.RenderedPdf(Assert.Single(responses), out _);

        var extracted = await ExtractAsync(
            9, string.Concat("{\"pdf\":\"", Convert.ToBase64String(merged), "\"}"));

        Assert.Equal(2, extracted.PageCount);
        Assert.Contains("Alpha marker", extracted.Pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("Beta marker", extracted.Pages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_text_answers_the_text_of_each_page()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var pdf = await RenderAsync(9, "= Quarterly marker");
        var extracted = await ExtractAsync(
            10,
            string.Concat("{\"pdf\":\"", Convert.ToBase64String(pdf), "\"}"));

        Assert.Equal(1, extracted.PageCount);
        Assert.Equal(1, extracted.Pages[0].Page);
        Assert.Contains("Quarterly marker", extracted.Pages[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_text_returns_the_text_of_a_signed_document()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var pdf = await RenderAsync(11, "= Signed marker");
        var certificate = Certificate(Password);

        var signedFrame = McpTestClient.Frames(McpTestClient.ToolCall(12, McpToolNames.SignDocument, string.Concat(
            "{\"pdf\":\"", Convert.ToBase64String(pdf),
            "\",\"certificate\":\"", Convert.ToBase64String(certificate),
            "\",\"password\":\"", Password, "\"}")));

        var signed = McpTestClient.RenderedPdf(Assert.Single(await McpTestClient.ExchangeAsync(signedFrame)), out _);

        var extracted = await ExtractAsync(
            13, string.Concat("{\"pdf\":\"", Convert.ToBase64String(signed), "\"}"));

        Assert.Contains("Signed marker", extracted.Pages[0].Text, StringComparison.Ordinal);
    }

    /// <summary>Renders one Typst document through the server and returns its PDF bytes.</summary>
    private static async Task<byte[]> RenderAsync(int id, string source)
    {
        var arguments = "{\"source\":\"" + source.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"}";
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(id, McpToolNames.RenderDocument, arguments)));

        return McpTestClient.RenderedPdf(Assert.Single(responses), out _);
    }

    /// <summary>Calls <c>extract_text</c> and parses the page list it answers with.</summary>
    private static async Task<Extracted> ExtractAsync(int id, string arguments)
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(id, McpToolNames.ExtractText, arguments)));

        var response = Assert.Single(responses);
        var result = response.GetProperty("result");

        Assert.False(result.TryGetProperty("isError", out _), result.GetRawText());

        using var document = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);

        var pages = document.RootElement.GetProperty("pages").EnumerateArray()
            .Select(page => (Page: page.GetProperty("page").GetInt32(), Text: page.GetProperty("text").GetString()!))
            .ToList();

        return new Extracted(document.RootElement.GetProperty("pageCount").GetInt32(), pages);
    }

    /// <summary>A self-signed PKCS#12 certificate with its private key, as the bytes of the file.</summary>
    private static byte[] Certificate(string password)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=M3scalinum.Text.Mcp Tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        return certificate.Export(X509ContentType.Pkcs12, password);
    }

    private sealed record Extracted(int PageCount, IReadOnlyList<(int Page, string Text)> Pages);
}

/// <summary>
/// The refusals of the three PDF tools: each one happens before a document is read, so none of these
/// needs the native library.
/// </summary>
public sealed class PdfToolArgumentTests
{
        private static Task<IReadOnlyList<JsonElement>> CallAsync(
            string tool,
            string arguments,
            McpServerOptions? options = null)
            => McpTestClient.ExchangeAsync(
                McpTestClient.Frames(McpTestClient.ToolCall(1, tool, arguments)),
                options);

        private static JsonElement Error(IReadOnlyList<JsonElement> responses)
            => Assert.Single(responses).GetProperty("error");

        [Fact]
        public async Task Sign_document_without_a_password_is_invalid_params()
        {
            var error = Error(await CallAsync(
                McpToolNames.SignDocument,
                """{"pdf":"JVBERi0xLjQK","certificate":"MIIB"}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("'password'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Sign_document_with_both_a_pdf_and_a_path_is_invalid_params()
        {
            var error = Error(await CallAsync(
                McpToolNames.SignDocument,
                """{"pdf":"JVBERi0xLjQK","pdfPath":"a.pdf","certificate":"MIIB","password":""}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("exactly one of 'pdf' or 'pdfPath'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Sign_document_refuses_a_path_when_file_output_is_disabled()
        {
            var error = Error(await CallAsync(
                McpToolNames.SignDocument,
                """{"pdfPath":"a.pdf","certificate":"MIIB","password":""}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("does not read files", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Sign_document_refuses_a_certificate_that_is_not_pkcs12()
        {
            var error = Error(await CallAsync(
                McpToolNames.SignDocument,
                """{"pdf":"JVBERi0xLjQK","certificate":"MIIB","password":"wrong"}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("PKCS#12", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Merge_documents_with_one_document_is_invalid_params()
        {
            var error = Error(await CallAsync(
                McpToolNames.MergeDocuments,
                """{"documents":["JVBERi0xLjQK"]}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("at least two", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Merge_documents_that_is_not_an_array_is_invalid_params()
        {
            var error = Error(await CallAsync(
                McpToolNames.MergeDocuments,
                """{"documents":"JVBERi0xLjQK"}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("array of strings", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Merge_documents_refuses_paths_when_file_output_is_disabled()
        {
            var error = Error(await CallAsync(
                McpToolNames.MergeDocuments,
                """{"documentPaths":["a.pdf","b.pdf"]}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("does not read files", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Extract_text_with_neither_a_pdf_nor_a_path_is_invalid_params()
        {
            var error = Error(await CallAsync(McpToolNames.ExtractText, "{}"));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("exactly one of 'pdf' or 'pdfPath'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Extract_text_refuses_arguments_that_are_not_base64()
        {
            var error = Error(await CallAsync(McpToolNames.ExtractText, """{"pdf":"not base64!"}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("base64", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Extract_text_refuses_a_path_when_file_output_is_disabled()
        {
            var error = Error(await CallAsync(McpToolNames.ExtractText, """{"pdfPath":"a.pdf"}"""));

            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("does not read files", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Extract_text_of_a_document_that_is_not_a_pdf_is_a_tool_error()
        {
            var responses = await CallAsync(
                McpToolNames.ExtractText,
                "{\"pdf\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not a PDF")) + "\"}");

            var result = Assert.Single(responses).GetProperty("result");

            Assert.True(result.GetProperty("isError").GetBoolean());
            Assert.Contains(
                "PDF", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Merge_documents_of_something_that_is_not_a_pdf_is_a_tool_error()
        {
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not a PDF"));

            var responses = await CallAsync(
                McpToolNames.MergeDocuments,
                "{\"documents\":[\"" + payload + "\",\"" + payload + "\"]}");

            var result = Assert.Single(responses).GetProperty("result");

            Assert.True(result.GetProperty("isError").GetBoolean());
        }
}
