using GradrTab.Models;
using GradrTab.Services;
using GradrTab.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GradrTab.Tests;

public class DocumentMapperTests
{
    [Fact]
    public void ToResponse_MapsCoreFields()
    {
        var document = new Document
        {
            OriginalFileName = "notes.txt",
            ContentType = "text/plain",
            FileExtension = ".txt",
            FileSizeBytes = 11,
            DocumentType = DocumentType.Text,
            Status = DocumentStatus.Completed,
            CharacterCount = 11,
            WordCount = 2,
            EntryCount = 1,
            Warnings = "[\"truncated\"]",
        };

        var dto = document.ToResponse();

        Assert.Equal("notes.txt", dto.OriginalFileName);
        Assert.Equal("Text", dto.DocumentType);
        Assert.Equal("Completed", dto.Status);
        Assert.Equal(["truncated"], dto.Warnings);
    }

    [Fact]
    public void ToResponse_ToleratesBadJson()
    {
        var document = new Document { Metadata = "{bad", Warnings = "{bad" };

        var dto = document.ToResponse();

        Assert.Null(dto.Metadata);
        Assert.Empty(dto.Warnings);
    }
}
