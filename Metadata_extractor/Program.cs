using Dicom;
using System.IO;

/// <summary>
/// Simple console app with minimal validation to extract the dicom elements that form the metadata into json file
/// </summary>
namespace Metadata_extractor
{
    class Program
    {
        static void Main(string[] args)
        {
            var sourceFolder = @"C:\dicomfiles";
            var destinationFolder = Path.Combine(sourceFolder, "MetadataToJson");
            Extract(sourceFolder, destinationFolder);
        }

        private static void Extract(string pathToDicomFiles, string metadataFolder)
        {
            IDicomMetadataExtractor ext = new DicomMetadataExtractorToJson();

            var files = Directory.GetFiles(pathToDicomFiles);
            Directory.CreateDirectory(metadataFolder);

            foreach (var file in files)
            {
                var filename = Path.GetFileNameWithoutExtension(file);

                var fileExt = Path.GetExtension(file);
                if (!string.Equals(fileExt, ".dcm", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var outputFile = Path.Combine(metadataFolder, filename + ".json");
                var dicomFile = DicomFile.Open(file);

                using (FileStream fileStream = File.Create(outputFile))
                {
                    ext.ExtractMetadata(dicomFile, fileStream);
                }

            }
        }
    }
}
