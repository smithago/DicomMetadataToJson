using Dicom;
using System;
using System.Collections.Generic;
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
            var sourceFolder = @"C:\dicomfiles\Output";
            List<string> dcmFiles = new List<string>();
            ProcessDirectory(sourceFolder, dcmFiles);
            var destinationFolder = Path.Combine(@"C:\dicomfiles\", "MetadataToJson2");
            Directory.CreateDirectory(destinationFolder);

            Extract(dcmFiles, destinationFolder);
        }

        public static void ProcessDirectory(string targetDirectory, List<string> dcmFiles)
        {
            // Process the list of files found in the directory.
            var fileEntries = Directory.GetFiles(targetDirectory);
            foreach (var file in fileEntries)
            {
                var fileExt = Path.GetExtension(file);
                if (!string.Equals(fileExt, ".dcm", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                dcmFiles.Add(file);
            }

            // Recurse into subdirectories of this directory.
            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
            foreach (string subdirectory in subdirectoryEntries)
            {
                ProcessDirectory(subdirectory, dcmFiles);
            }
        }

        private static void Extract(List<string> dicomFiles, string outputPath)
        {
            IDicomMetadataExtractor ext = new DicomMetadataExtractorToJson();

            
            foreach (var file in dicomFiles)
            {
                var filename = Path.GetFileNameWithoutExtension(file);

                var ran = DateTime.Now.ToString("yyyyMMddHHmmssffff");
                var outputFile = Path.Combine(outputPath, filename + ran + ".json");
                var dicomFile = DicomFile.Open(file);

                using (FileStream fileStream = File.Create(outputFile))
                {
                    ext.ExtractMetadata(dicomFile, fileStream);
                }

            }
        }
    }
}
