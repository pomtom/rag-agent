-- The two tables of the application. Safe to run many times: it only creates what is missing.
-- Run automatically at startup by DatabaseInitializer.cs (you can also run it yourself in SSMS).
--
-- Needs SQL Server 2025 (or Azure SQL) for the VECTOR type.

-- 1. One row per uploaded file + "did we generate embeddings for it?"
IF OBJECT_ID(N'dbo.Documents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Documents
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
        FileName      NVARCHAR(260)  NOT NULL,
        FileExtension NVARCHAR(10)   NOT NULL,                  -- .pdf, .docx, .xlsx, .csv, .md, .txt
        ContentType   NVARCHAR(100)  NULL,                      -- what the browser said, e.g. application/pdf
        FileSizeBytes BIGINT         NOT NULL,
        FileHash      CHAR(64)       NOT NULL,                  -- SHA-256 of the content: spots the same file uploaded twice
        FileContent   VARBINARY(MAX) NOT NULL,                  -- the original file; the background service reads it from here
        Status        NVARCHAR(20)   NOT NULL CONSTRAINT DF_Documents_Status DEFAULT N'Pending',
        ChunkCount    INT            NOT NULL CONSTRAINT DF_Documents_ChunkCount DEFAULT 0,
        ErrorMessage  NVARCHAR(MAX)  NULL,                      -- why ingestion failed (Status = Failed)
        UploadedAt    DATETIME2      NOT NULL CONSTRAINT DF_Documents_UploadedAt DEFAULT SYSUTCDATETIME(),
        ProcessedAt   DATETIME2      NULL,                      -- when ingestion finished (Completed or Failed)

        CONSTRAINT CK_Documents_Status CHECK (Status IN (N'Pending', N'Processing', N'Completed', N'Failed'))
    );

    CREATE UNIQUE INDEX UX_Documents_FileHash ON dbo.Documents(FileHash);
    CREATE INDEX IX_Documents_Status ON dbo.Documents(Status);
END;

-- 2. The pieces of text ("chunks") of each file and their embedding vectors.
IF OBJECT_ID(N'dbo.DocumentChunks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentChunks
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentChunks PRIMARY KEY,
        DocumentId  INT           NOT NULL
                    CONSTRAINT FK_DocumentChunks_Documents REFERENCES dbo.Documents(Id) ON DELETE CASCADE,
        ChunkIndex  INT           NOT NULL,                     -- 0, 1, 2 ... the order inside the file
        Content     NVARCHAR(MAX) NOT NULL,                     -- the text the chat model will read
        Embedding   VECTOR(1536)  NOT NULL,                     -- text-embedding-3-small returns 1536 numbers
        CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_DocumentChunks_CreatedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_DocumentChunks_DocumentId ON dbo.DocumentChunks(DocumentId);
END;
