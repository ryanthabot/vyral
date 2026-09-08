using System.Text;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Integrations.Obs;

public sealed record ObsImportResult(bool Success, string Message, ObsSceneCollection? Collection, ObsSceneCollectionDocument? Document);

/// <summary>
/// Imports OBS scene-collection files VERBATIM: the raw file bytes are the canonical
/// store (no conversion, unknown/future fields survive), everything else is derived.
/// Export writes those exact bytes back — byte-identical guarantee.
/// </summary>
public sealed class ObsSceneCollectionStore
{
    private readonly IDataService data;

    public ObsSceneCollectionStore(IDataService data)
    {
        this.data = data;
    }

    public async Task<ObsImportResult> ImportFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ObsImportResult(false, $"File not found: {filePath}", null, null);
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            ObsSceneCollectionDocument document = ObsSceneCollectionDocument.Parse(Encoding.UTF8.GetString(bytes));
            ObsSceneCollection entity = new()
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                RawJsonBytes = bytes,
                SourcePath = filePath,
                SceneCount = document.Scenes.Count,
                SourceCount = document.SourceCount,
            };
            ObsSceneCollection saved = await data.SaveObsSceneCollectionAsync(entity).ConfigureAwait(false);
            return new ObsImportResult(
                true,
                $"Imported '{entity.Name}' — {document.Scenes.Count} scene(s), {document.SourceCount} source(s).",
                saved,
                document);
        }
        catch (InvalidDataException ex)
        {
            return new ObsImportResult(false, ex.Message, null, null);
        }
        catch (Exception ex)
        {
            return new ObsImportResult(false, $"Import failed: {ex.Message}", null, null);
        }
    }

    public async Task<(bool Success, string Message)> ExportToFileAsync(int id, string destinationPath)
    {
        ObsSceneCollection? entity = await data.GetObsSceneCollectionAsync(id).ConfigureAwait(false);
        if (entity is null)
        {
            return (false, "Collection not found.");
        }

        try
        {
            await File.WriteAllBytesAsync(destinationPath, entity.RawJsonBytes).ConfigureAwait(false);
            return (true, $"Exported to {destinationPath}");
        }
        catch (Exception ex)
        {
            return (false, $"Export failed: {ex.Message}");
        }
    }

    public async Task<(ObsSceneCollectionDocument? Document, string? Error)> LoadDocumentAsync(int id)
    {
        ObsSceneCollection? entity = await data.GetObsSceneCollectionAsync(id).ConfigureAwait(false);
        if (entity is null)
        {
            return (null, "Collection not found.");
        }

        try
        {
            return (ObsSceneCollectionDocument.Parse(Encoding.UTF8.GetString(entity.RawJsonBytes)), null);
        }
        catch (InvalidDataException ex)
        {
            return (null, ex.Message);
        }
    }
}
