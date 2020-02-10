using Dicom;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Metadata_extractor
{
    public class DicomMetadataExtractorToJson: IDicomMetadataExtractor
    {
        public void ExtractMetadata(DicomFile dicomFile, FileStream outputFile)
        {
            //Get the dataSets with just metadata
            GetMetadataDataset(dicomFile);

            //convert to json and save to output
            WriteToFile(dicomFile.Dataset, outputFile);
        }

        private void WriteToFile(DicomDataset dataset, FileStream outputFile)
        {
            //using system.text.json with custom serializer
            //var options = new JsonSerializerOptions
            //{
            //    WriteIndented = true,
            //};
            //var jsonString = JsonSerializer.Serialize<DicomDataset>(dicomFile.Dataset, options);

            //File.WriteAllText(outputFilePath, jsonString);

            var jsonSerializer = new JsonSerializer();
            jsonSerializer.Converters.Add(new JsonDicomConverter(writeTagsAsKeywords: true, skipVr: true));

            using (Stream stream = new MemoryStream())
            using (var streamWriter = new StreamWriter(stream, Encoding.UTF8))
            using (var jsonTextWriter = new JsonTextWriter(streamWriter))
            {
                jsonSerializer.Serialize(jsonTextWriter, dataset);
                jsonTextWriter.Flush();

                stream.Seek(0, SeekOrigin.Begin);
                stream.CopyTo(outputFile);
            }
        }

        private void GetMetadataDataset(DicomFile dicomFile)
        {
            var tagsToRemove = new List<DicomTag>();
            RecursivelyFindLargeDataElements(dicomFile.Dataset, tagsToRemove);

            dicomFile.Dataset.Remove(tagsToRemove.ToArray());
        }

        private void RecursivelyFindLargeDataElements(DicomDataset dataset, IList<DicomTag> tagsToRemove)
        {
            foreach (var dataElement in dataset)
            {
                if (dataElement.ValueRepresentation == DicomVR.SQ)
                {
                    var sequenceItem = dataElement as DicomSequence;
                    foreach (DicomDataset childDataSet in sequenceItem.Items)
                    {
                        RecursivelyFindLargeDataElements(childDataSet, tagsToRemove);
                    }
                }
                else if (LargeValueRepresentations.Contains(dataElement.ValueRepresentation))
                {
                    tagsToRemove.Add(dataElement.Tag);
                }
            }
        }

        private static readonly HashSet<DicomVR> LargeValueRepresentations = new HashSet<DicomVR>()
        {
            DicomVR.OB,
            DicomVR.OD,
            DicomVR.OF,
            DicomVR.OL,
            DicomVR.OW,
            DicomVR.UN,
        };
    }
}
