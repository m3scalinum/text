using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp.Internal;
using M3scalinum.Text.Pdf;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Templates;
using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Mcp;

/// <summary>
/// A Model Context Protocol server that renders documents, and post-processes PDFs,
/// speaking JSON-RPC 2.0 over a transport the host chooses.
/// </summary>
/// <remarks>
/// <para>
/// The server is the protocol core: <see cref="HandleMessageAsync"/> takes one message and returns one
/// answer, and it never sees a stream, a header or a socket. <see cref="IMcpTransport"/> is the
/// framing — <see cref="StdioTransport"/> (one message per line, bound to the process's pipes by
/// <see cref="RunStdioAsync"/>) or <see cref="McpHttpServer"/> (the streamable-HTTP shape). It serves
/// five tools — <c>render_document</c>, <c>list_templates</c>, <c>sign_document</c>,
/// <c>merge_documents</c> and <c>extract_text</c> — and it holds no state between
/// messages.
/// </para>
/// <para>
/// The security posture is the point of the package. A tool call is authored by a model, so it can
/// name a path, ask for a layout that does not exist, or send a document that never finishes
/// compiling. The server reads no file it was not told to read, writes nothing unless the host
/// enabled file output, opens no socket, and applies <see cref="McpServerOptions"/>'s limits before
/// it compiles anything.
/// </para>
/// </remarks>
public sealed class McpServer : IMcpMessageHandler
{
    /// <summary>The newest protocol revision this server speaks.</summary>
    public const string ProtocolVersion = "2025-06-18";

    internal const int ParseError = -32700;
    internal const int InvalidRequest = -32600;
    internal const int MethodNotFound = -32601;
    internal const int InvalidParams = -32602;

    private static readonly string[] KnownProtocolVersions = ["2024-11-05", "2025-03-26", ProtocolVersion];

    private readonly McpServerOptions _options;
    private readonly TemplateCatalog _catalog;
    private readonly Lazy<TypstCompiler> _compiler;
    private readonly string _serverVersion;

    /// <summary>Creates a server with the default limits, the whole layout pack, and no file output.</summary>
    public McpServer()
        : this(null)
    {
    }

    /// <summary>Creates a server.</summary>
    /// <param name="options">
    /// The limits and permissions; <see langword="null"/> uses <see cref="McpServerOptions"/>'s
    /// defaults.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A limit cannot work, or an allowed template id is not in the pack.
    /// </exception>
    public McpServer(McpServerOptions? options)
        : this(options ?? new McpServerOptions(), TemplateCatalog.Default, static () => new TypstCompiler())
    {
    }

    /// <summary>Creates a server over an explicit catalog and compiler factory, for a test or a host.</summary>
    internal McpServer(McpServerOptions options, TemplateCatalog catalog, Func<TypstCompiler> compiler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(compiler);

        options.Validate(catalog);

        _options = options;
        _catalog = catalog;
        _compiler = new Lazy<TypstCompiler>(compiler, LazyThreadSafetyMode.ExecutionAndPublication);
        _serverVersion = options.ServerVersion
            ?? typeof(McpServer).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    /// <summary>The limits this server applies.</summary>
    public McpServerOptions Options => _options;

    /// <summary>
    /// Serves the protocol on <paramref name="input"/> until the stream ends.
    /// </summary>
    /// <param name="input">Where requests are read from, one JSON message per line.</param>
    /// <param name="output">Where responses are written, one JSON message per line.</param>
    /// <param name="cancellationToken">Stops the loop; the server writes no farewell message.</param>
    /// <remarks>
    /// The loop is sequential: messages are answered in the order they arrive, which is what a stdio
    /// transport implies. End of input is a clean shutdown — the method returns, and the host exits.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task RunAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        return RunAsync(new StdioTransport(input, output, _options.MaxRequestSize), cancellationToken);
    }

    /// <summary>Serves the protocol over a transport the host chose.</summary>
    /// <param name="transport">The framing: stdio, streamable HTTP, or one the host wrote.</param>
    /// <param name="cancellationToken">Stops the transport.</param>
    /// <remarks>
    /// This is the whole of the hosting model: the protocol core never sees a stream, so a transport
    /// can be added without touching dispatch. <see cref="McpServer"/> is the
    /// <see cref="IMcpMessageHandler"/> the transport drives.
    /// </remarks>
    public Task RunAsync(IMcpTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);

        return transport.RunAsync(this, cancellationToken);
    }

    /// <summary>
    /// Handles one JSON-RPC message and answers it, without knowing how it arrived.
    /// </summary>
    /// <param name="message">One complete JSON-RPC message.</param>
    /// <param name="cancellationToken">Ends the work when the transport is closing.</param>
    /// <returns>
    /// The response as UTF-8 JSON, or <see langword="null"/> when the message is a notification or is
    /// otherwise not to be answered.
    /// </returns>
    /// <remarks>
    /// The bytes carry the message only: framing — the newline of stdio, the headers and status of
    /// HTTP — belongs to the transport.
    /// </remarks>
    public async Task<byte[]?> HandleMessageAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var buffer = new MemoryStream(256);

        var answered = await HandleAsync(message, buffer, cancellationToken).ConfigureAwait(false);

        return answered ? buffer.ToArray() : null;
    }

    /// <summary>Serves the protocol on the process's standard input and output.</summary>
    /// <param name="options">The limits and permissions; <see langword="null"/> uses the defaults.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <remarks>
    /// A host wires this to <c>PublishAot</c> without a JSON reflection path: the byte streams come
    /// from <see cref="Console"/>, nothing is read from the console's encoding.
    /// </remarks>
    public static Task RunStdioAsync(
        McpServerOptions? options = null,
        CancellationToken cancellationToken = default)
        => new McpServer(options).RunAsync(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            cancellationToken);

    /// <summary>Handles one message: a request is answered, a notification is not.</summary>
    private async Task<bool> HandleAsync(string line, Stream output, CancellationToken cancellationToken)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(
                output, null, ParseError, "parse error: the message is not valid JSON", cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                await WriteErrorAsync(
                    output,
                    null,
                    InvalidRequest,
                    "invalid request: MCP has no batch requests",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                await WriteErrorAsync(
                    output,
                    null,
                    InvalidRequest,
                    "invalid request: a JSON-RPC message is an object",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            var hasId = root.TryGetProperty("id", out var idElement)
                && idElement.ValueKind is not JsonValueKind.Null;
            JsonElement? id = hasId ? idElement.Clone() : null;
            var isRequest = hasId;

            if (!root.TryGetProperty("jsonrpc", out var version)
                || version.ValueKind != JsonValueKind.String
                || !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal))
            {
                if (isRequest)
                {
                    await WriteErrorAsync(
                        output, id, InvalidRequest, "invalid request: 'jsonrpc' must be \"2.0\"", cancellationToken)
                        .ConfigureAwait(false);
                }

                return isRequest;
            }

            if (!root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String)
            {
                if (isRequest)
                {
                    await WriteErrorAsync(
                        output, id, InvalidRequest, "invalid request: 'method' must be a string", cancellationToken)
                        .ConfigureAwait(false);
                }

                return isRequest;
            }

            var method = methodElement.GetString() ?? string.Empty;
            var parameters = root.TryGetProperty("params", out var paramsElement)
                && paramsElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? (JsonElement?)paramsElement.Clone()
                    : null;

            // A notification carries no id: the protocol says never to answer one, so nothing below
            // writes a message when there is nothing to answer.
            if (!isRequest)
            {
                return false;
            }

            switch (method)
            {
                case "initialize":
                    await WriteResultAsync(
                        output,
                        id,
                        Initialize(Negotiate(parameters)),
                        McpJsonContext.Default.InitializeResultDto,
                        cancellationToken).ConfigureAwait(false);
                    return true;

                case "ping":
                    await WriteResultAsync(
                        output,
                        id,
                        new EmptyResultDto(),
                        McpJsonContext.Default.EmptyResultDto,
                        cancellationToken).ConfigureAwait(false);
                    return true;

                case "tools/list":
                    await WriteResultAsync(
                        output,
                        id,
                        ListTools(),
                        McpJsonContext.Default.ToolsListResultDto,
                        cancellationToken).ConfigureAwait(false);
                    return true;

                case "tools/call":
                    await CallToolAsync(output, id, parameters, cancellationToken).ConfigureAwait(false);
                    return true;

                // Lifecycle notifications never reach here: a message without an id is dropped
                // before the dispatch, as the protocol requires.
                default:
                    await WriteErrorAsync(
                        output,
                        id,
                        MethodNotFound,
                        $"method not found: '{method}'",
                        cancellationToken).ConfigureAwait(false);
                    return true;
            }
        }
    }

    /// <summary>
    /// Answers <c>tools/call</c>, in its three outcomes: result, invalid arguments, failure.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A tool boundary turns any failure of the work into a tool error result, "
            + "so the client sees the reason instead of the session ending.")]
    private async Task CallToolAsync(
        Stream output,
        JsonElement? id,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } call)
        {
            await WriteErrorAsync(
                output,
                id,
                InvalidParams,
                "invalid params: 'tools/call' expects an object with 'name' and 'arguments'",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!call.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            await WriteErrorAsync(
                output, id, InvalidParams, "invalid params: 'tools/call' needs a tool name", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var name = nameElement.GetString() ?? string.Empty;
        var arguments = call.TryGetProperty("arguments", out var argumentsElement)
            && argumentsElement.ValueKind is JsonValueKind.Object
                ? argumentsElement
                : default;

        ToolCallResultDto result;

        try
        {
            result = await InvokeAsync(name, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (McpToolArgumentsException exception)
        {
            await WriteErrorAsync(
                output, id, InvalidParams, $"invalid params: {exception.Message}", cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The call was well formed and the work failed: that is a tool error, not a protocol
            // error. The client sees what went wrong and can change the call.
            result = Failure(exception.Message);
        }

        await WriteResultAsync(
            output, id, result, McpJsonContext.Default.ToolCallResultDto, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Dispatches a well-formed tool call.</summary>
    private async Task<ToolCallResultDto> InvokeAsync(
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken) => name switch
        {
            McpToolNames.RenderDocument => await RenderDocumentAsync(arguments, cancellationToken).ConfigureAwait(false),
            McpToolNames.ListTemplates => ListTemplates(),
            McpToolNames.SignDocument => await SignDocumentAsync(arguments, cancellationToken).ConfigureAwait(false),
            McpToolNames.MergeDocuments => await MergeDocumentsAsync(arguments, cancellationToken).ConfigureAwait(false),
            McpToolNames.ExtractText => ExtractText(arguments),
            _ => throw new McpToolArgumentsException(
                $"unknown tool '{name}'; this server serves {McpToolNames.RenderDocument}, "
                + $"{McpToolNames.ListTemplates}, {McpToolNames.SignDocument}, "
                + $"{McpToolNames.MergeDocuments} and {McpToolNames.ExtractText}"),
        };

    /// <summary>Renders Typst source, or one layout of the pack with its parameters.</summary>
    private async Task<ToolCallResultDto> RenderDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var source = OptionalString(arguments, "source");
        var template = OptionalString(arguments, "template");
        var version = OptionalString(arguments, "version");
        var outputPath = ConfirmedOutputPath(arguments);

        if ((source is null) == (template is null))
        {
            throw new McpToolArgumentsException(
                "exactly one of 'source' or 'template' is required");
        }

        byte[] pdf;
        int? pageCount;

        if (source is not null)
        {
            EnsureDocumentSize(source, "'source'");

            using var world = new TypstWorldBuilder().AddSource("main.typ", source).Build();
            using var result = _compiler.Value.Compile(
                world, new TypstCompilationOptions { Timeout = _options.RenderTimeout });

            pdf = result.ExportPdf();
            pageCount = result.PageCount;
        }
        else
        {
            pdf = RenderTemplate(template!, version, OptionalElement(arguments, "parameters"), out pageCount);
        }

        return await DeliverAsync(pdf, pageCount, outputPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renders one layout, resolving it against the ids the host allowed.</summary>
    private byte[] RenderTemplate(
        string template,
        string? version,
        JsonElement? parameters,
        out int? pageCount)
    {
        if (!IsAllowed(template))
        {
            throw new McpToolArgumentsException(
                $"template '{template}' is not served by this server");
        }

        if (!_catalog.TryGet(template, version, out var descriptor))
        {
            throw new McpToolArgumentsException(
                version is null
                    ? $"no template '{template}' in the pack"
                    : $"no template '{template}' version '{version}' in the pack");
        }

        var values = TemplateArguments.Convert(descriptor.Manifest, parameters);

        using var result = descriptor.Template.RenderDocument(
            _compiler.Value, values, TypstDocumentKind.Paged);

        pageCount = result.PageCount;
        return result.ExportPdf();
    }

    /// <summary>Lists the layouts the host allowed, with their parameters.</summary>
    private ToolCallResultDto ListTemplates()
    {
        var templates = _catalog.Templates
            .Where(descriptor => IsAllowed(descriptor.Id))
            .Select(descriptor => new TemplateEntryDto
            {
                Id = descriptor.Id,
                Version = descriptor.Version,
                Description = descriptor.Description,
                Languages = descriptor.Languages.Select(LanguageName).ToArray(),
                Parameters = descriptor.Manifest.Parameters.Values
                    .Select(parameter => new TemplateParameterDto
                    {
                        Name = parameter.Name,
                        Type = TypeName(parameter.Type),
                        Required = parameter.Required,
                    })
                    .ToArray(),
            })
            .ToArray();

        var json = JsonSerializer.Serialize(
            new TemplateListDto { Templates = templates }, McpJsonContext.Default.TemplateListDto);

        return new ToolCallResultDto
        {
            Content = [new ContentBlockDto { Type = "text", Text = json }],
        };
    }

    /// <summary>Signs a PDF with a PKCS#12 certificate, as a PAdES signature.</summary>
    private async Task<ToolCallResultDto> SignDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var outputPath = ConfirmedOutputPath(arguments);
        var pdf = ReadBinary(arguments, "pdf", "pdfPath");
        var certificate = ReadBinary(arguments, "certificate", "certificatePath");
        var password = OptionalString(arguments, "password")
            ?? throw new McpToolArgumentsException(
                "'password' is required; pass an empty string when the certificate carries none");

        using var signing = LoadCertificate(certificate, password);

        var options = new PdfSignatureOptions
        {
            Reason = OptionalString(arguments, "reason"),
            Location = OptionalString(arguments, "location"),
        };

        var signed = PdfSigner.Sign(pdf, signing, options);

        return await DeliverAsync(signed, CountPages(signed), outputPath, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Merges two or more PDFs into one, in the order the call gives them.</summary>
    private async Task<ToolCallResultDto> MergeDocumentsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var outputPath = ConfirmedOutputPath(arguments);
        var documents = ReadBinaryArray(arguments, "documents", "documentPaths");

        if (documents.Count < 2)
        {
            throw new McpToolArgumentsException(
                $"merging needs at least two PDFs; this call carried {documents.Count}");
        }

        var merged = PdfPages.Merge([.. documents]);

        return await DeliverAsync(merged, CountPages(merged), outputPath, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the text each page of a PDF draws, as one JSON text block.</summary>
    private ToolCallResultDto ExtractText(JsonElement arguments)
    {
        var pdf = ReadBinary(arguments, "pdf", "pdfPath");
        var extracted = PdfText.Extract(pdf);

        var result = new ExtractedTextDto
        {
            PageCount = extracted.PageCount,
            Pages = extracted.Pages
                .Select(page => new ExtractedPageDto { Page = page.PageNumber, Text = page.Text })
                .ToArray(),
        };

        return new ToolCallResultDto
        {
            Content =
            [
                new ContentBlockDto
                {
                    Type = "text",
                    Text = JsonSerializer.Serialize(result, McpJsonContext.Default.ExtractedTextDto),
                },
            ],
        };
    }

    /// <summary>
    /// Reads one binary argument, inline as base64 or from a path the host allowed, and refuses both
    /// at once or neither.
    /// </summary>
    private byte[] ReadBinary(JsonElement arguments, string inlineName, string pathName)
    {
        var inline = OptionalString(arguments, inlineName);
        var path = OptionalString(arguments, pathName);

        if ((inline is null) == (path is null))
        {
            throw new McpToolArgumentsException(
                $"exactly one of '{inlineName}' or '{pathName}' is required");
        }

        return inline is not null ? Decode(inline, inlineName) : ReadFile(path!, pathName);
    }

    /// <summary>Reads an array of PDFs, inline as base64 or from paths, in the order given.</summary>
    private List<byte[]> ReadBinaryArray(JsonElement arguments, string inlineName, string pathName)
    {
        var inline = OptionalArray(arguments, inlineName);
        var paths = OptionalArray(arguments, pathName);

        if ((inline is null) == (paths is null))
        {
            throw new McpToolArgumentsException(
                $"exactly one of '{inlineName}' or '{pathName}' is required");
        }

        var documents = new List<byte[]>(inline?.Count ?? paths!.Count);

        if (inline is not null)
        {
            for (var index = 0; index < inline.Count; index++)
            {
                documents.Add(Decode(inline[index], $"{inlineName}[{index}]"));
            }

            return documents;
        }

        for (var index = 0; index < paths!.Count; index++)
        {
            documents.Add(ReadFile(paths[index], $"{pathName}[{index}]"));
        }

        return documents;
    }

    /// <summary>Decodes a base64 argument, refusing a value that is not base64 or is oversized.</summary>
    private byte[] Decode(string value, string argument)
    {
        // Four base64 characters carry three bytes, so this is the most the value can decode to.
        var buffer = new byte[(value.Length / 4 * 3) + 3];

        if (!Convert.TryFromBase64String(value, buffer, out var written))
        {
            throw new McpToolArgumentsException($"'{argument}' must be base64-encoded binary data");
        }

        EnsureBinarySize(written, argument);
        return buffer.AsSpan(0, written).ToArray();
    }

    /// <summary>Reads a file the call named, only when the host allows file access.</summary>
    private byte[] ReadFile(string path, string argument)
    {
        if (path.Length == 0)
        {
            throw new McpToolArgumentsException($"'{argument}' must not be empty");
        }

        if (!_options.AllowFileOutput)
        {
            throw new McpToolArgumentsException(
                $"this server does not read files: pass '{argument}' inline as base64, "
                + "or have the host enable file output");
        }

        if (!File.Exists(path))
        {
            throw new McpToolArgumentsException($"'{argument}' does not name an existing file");
        }

        var length = new FileInfo(path).Length;

        if (length > _options.MaxDocumentSize)
        {
            throw new McpToolArgumentsException(
                $"'{argument}' is {length} bytes; this server accepts at most {_options.MaxDocumentSize}");
        }

        return File.ReadAllBytes(path);
    }

    /// <summary>Refuses a binary document larger than the host allows.</summary>
    private void EnsureBinarySize(int size, string argument)
    {
        if (size > _options.MaxDocumentSize)
        {
            throw new McpToolArgumentsException(
                $"'{argument}' is {size} bytes; this server accepts at most {_options.MaxDocumentSize}");
        }
    }

    /// <summary>Loads the PKCS#12 a call carried, keeping its private key in memory only.</summary>
    private static X509Certificate2 LoadCertificate(byte[] pkcs12, string password)
    {
        try
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pkcs12, password, X509KeyStorageFlags.EphemeralKeySet);
#else
            return new X509Certificate2(pkcs12, password, X509KeyStorageFlags.EphemeralKeySet);
#endif
        }
        catch (CryptographicException exception)
        {
            throw new McpToolArgumentsException(
                "'certificate' is not a readable PKCS#12 file; check the file and the password",
                exception);
        }
    }

    /// <summary>The page count of a PDF, when it can be read; <see langword="null"/> otherwise.</summary>
    private static int? CountPages(byte[] pdf)
    {
        try
        {
            return PdfText.Extract(pdf).PageCount;
        }
        catch (PdfTextException)
        {
            return null;
        }
        catch (PdfOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the PDF, either as an embedded base64 resource or written to the path the caller named.
    /// </summary>
    private static async Task<ToolCallResultDto> DeliverAsync(
        byte[] pdf,
        int? pageCount,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        if (outputPath is not null)
        {
            await File.WriteAllBytesAsync(outputPath, pdf, cancellationToken).ConfigureAwait(false);

            var written = JsonSerializer.Serialize(
                new RenderSummaryDto { ByteLength = pdf.Length, PageCount = pageCount, Path = outputPath },
                McpJsonContext.Default.RenderSummaryDto);

            return new ToolCallResultDto
            {
                Content = [new ContentBlockDto { Type = "text", Text = written }],
            };
        }

        var summary = JsonSerializer.Serialize(
            new RenderSummaryDto { ByteLength = pdf.Length, PageCount = pageCount },
            McpJsonContext.Default.RenderSummaryDto);

        return new ToolCallResultDto
        {
            Content =
            [
                new ContentBlockDto { Type = "text", Text = summary },
                new ContentBlockDto
                {
                    Type = "resource",
                    Resource = new EmbeddedResourceDto
                    {
                        Uri = $"m3scalinum://documents/{Guid.NewGuid():N}.pdf",
                        MimeType = "application/pdf",
                        Blob = Convert.ToBase64String(pdf),
                    },
                },
            ],
        };
    }

    /// <summary>The <c>initialize</c> result, echoing the revision the client asked for when known.</summary>
    private InitializeResultDto Initialize(string protocolVersion) => new()
    {
        ProtocolVersion = protocolVersion,
        Capabilities = new ServerCapabilitiesDto
        {
            Tools = new ToolCapabilitiesDto { ListChanged = false },
        },
        ServerInfo = new ImplementationDto
        {
            Name = _options.ServerName,
            Version = _serverVersion,
        },
    };

    /// <summary>The <c>tools/list</c> result.</summary>
    private static ToolsListResultDto ListTools() => new()
    {
        Tools =
        [
            new ToolDefinitionDto
            {
                Name = McpToolNames.RenderDocument,
                Description =
                    "Render a PDF from Typst source, or from one layout of M3scalinum.Text.Templates "
                    + "with the parameters list_templates declares. Returns the PDF as a base64 "
                    + "embedded resource, or writes it to 'outputPath' when the host allows file output.",
                InputSchema = McpToolSchemas.RenderDocument,
            },
            new ToolDefinitionDto
            {
                Name = McpToolNames.ListTemplates,
                Description =
                    "List the business-document layouts this server serves: their ids, versions, "
                    + "languages and declared parameters.",
                InputSchema = McpToolSchemas.ListTemplates,
            },
            new ToolDefinitionDto
            {
                Name = McpToolNames.SignDocument,
                Description =
                    "Sign a PDF with a PKCS#12 certificate, as a PAdES-B signature, and return the "
                    + "signed file. Pass the PDF base64-encoded, or name a path when the host allows "
                    + "file output; the password is required, and an empty one is accepted.",
                InputSchema = McpToolSchemas.SignDocument,
            },
            new ToolDefinitionDto
            {
                Name = McpToolNames.MergeDocuments,
                Description =
                    "Merge two or more PDFs into one, in the order given, keeping every page's "
                    + "content, fonts, images and annotations.",
                InputSchema = McpToolSchemas.MergeDocuments,
            },
            new ToolDefinitionDto
            {
                Name = McpToolNames.ExtractText,
                Description =
                    "Read the text each page of a PDF draws, through the fonts' ToUnicode maps, and "
                    + "answer it per page. A page whose text cannot be decoded is reported empty.",
                InputSchema = McpToolSchemas.ExtractText,
            },
        ],
    };

    /// <summary>The revision this server will speak, given what the client asked for.</summary>
    private static string Negotiate(JsonElement? parameters)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } request
            || !request.TryGetProperty("protocolVersion", out var requested)
            || requested.ValueKind != JsonValueKind.String)
        {
            return ProtocolVersion;
        }

        var version = requested.GetString();

        return version is not null && KnownProtocolVersions.Contains(version, StringComparer.Ordinal)
            ? version
            : ProtocolVersion;
    }

    /// <summary>Reads a string argument, refusing a value of another type.</summary>
    private static string? OptionalString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new McpToolArgumentsException($"'{name}' must be a string");
        }

        return value.GetString();
    }

    /// <summary>Reads a structured argument, or <see langword="null"/> when it is absent.</summary>
    private static JsonElement? OptionalElement(JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out var value)
            && value.ValueKind is not JsonValueKind.Null
                ? value
                : null;

    /// <summary>Reads an array of strings, or <see langword="null"/> when it is absent.</summary>
    private static List<string>? OptionalArray(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new McpToolArgumentsException($"'{name}' must be an array of strings");
        }

        var items = new List<string>();

        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new McpToolArgumentsException($"'{name}' must be an array of strings");
            }

            items.Add(element.GetString() ?? string.Empty);
        }

        return items;
    }

    /// <summary>
    /// Reads <c>outputPath</c> and refuses it outright when the host disabled file output.
    /// </summary>
    /// <remarks>
    /// Checked before any compilation, and refused rather than ignored: a caller that asked for a
    /// file and got bytes would believe the file exists.
    /// </remarks>
    private string? ConfirmedOutputPath(JsonElement arguments)
    {
        var path = OptionalString(arguments, "outputPath");

        if (path is null)
        {
            return null;
        }

        if (path.Length == 0)
        {
            throw new McpToolArgumentsException("'outputPath' must not be empty");
        }

        if (!_options.AllowFileOutput)
        {
            throw new McpToolArgumentsException(
                "this server does not write files: omit 'outputPath' to receive the PDF bytes");
        }

        return path;
    }

    /// <summary>Refuses a document larger than the host allows, before it is compiled.</summary>
    private void EnsureDocumentSize(string document, string argument)
    {
        var size = Encoding.UTF8.GetByteCount(document);

        if (size > _options.MaxDocumentSize)
        {
            throw new McpToolArgumentsException(
                $"{argument} is {size} bytes; this server accepts at most {_options.MaxDocumentSize}");
        }
    }

    /// <summary>Whether the host allows rendering this layout.</summary>
    private bool IsAllowed(string id)
        => _options.AllowedTemplateIds is null
            || _options.AllowedTemplateIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    private static string LanguageName(TemplateLanguage language) => language switch
    {
        TemplateLanguage.English => "en",
        TemplateLanguage.French => "fr",
        _ => language.ToString(),
    };

    private static string TypeName(TypstParameterType type) => type switch
    {
        TypstParameterType.String => "string",
        TypstParameterType.Number => "number",
        TypstParameterType.Boolean => "boolean",
        TypstParameterType.Date => "date",
        TypstParameterType.Asset => "asset",
        TypstParameterType.Array => "array",
        TypstParameterType.Object => "object",
        _ => "unknown",
    };

    private static ToolCallResultDto Failure(string message) => new()
    {
        IsError = true,
        Content = [new ContentBlockDto { Type = "text", Text = message }],
    };

    /// <summary>Writes a JSON-RPC result.</summary>
    private static Task WriteResultAsync<T>(
        Stream output,
        JsonElement? id,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        => WriteMessageAsync(
            output,
            writer =>
            {
                WritePrologue(writer, id);
                writer.WritePropertyName("result");
                JsonSerializer.Serialize(writer, value, typeInfo);
            },
            cancellationToken);

    /// <summary>Writes a JSON-RPC error object.</summary>
    private static Task WriteErrorAsync(
        Stream output,
        JsonElement? id,
        int code,
        string message,
        CancellationToken cancellationToken)
        => WriteMessageAsync(
            output,
            writer =>
            {
                WritePrologue(writer, id);
                writer.WritePropertyName("error");
                writer.WriteStartObject();
                writer.WriteNumber("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();
            },
            cancellationToken);

    /// <summary>Writes the two members every message starts with.</summary>
    private static void WritePrologue(Utf8JsonWriter writer, JsonElement? id)
    {
        writer.WriteString("jsonrpc", "2.0");
        writer.WritePropertyName("id");

        if (id is { } value)
        {
            value.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    /// <summary>Writes one message into the output, unframed and unflushed.</summary>
    private static async Task WriteMessageAsync(
        Stream output,
        Action<Utf8JsonWriter> writeBody,
        CancellationToken cancellationToken)
    {
        await output
            .WriteAsync(BuildMessage(writeBody), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Builds one JSON-RPC message as UTF-8 bytes, without any framing.</summary>
    private static byte[] BuildMessage(Action<Utf8JsonWriter> writeBody)
    {
        using var buffer = new MemoryStream(1024);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Builds a JSON-RPC error message for a transport that must answer before the core sees the
    /// message — an oversized line, a body the server refused to read.
    /// </summary>
    internal static byte[] FrameError(JsonElement? id, int code, string message)
        => BuildMessage(writer =>
        {
            WritePrologue(writer, id);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        });
}
