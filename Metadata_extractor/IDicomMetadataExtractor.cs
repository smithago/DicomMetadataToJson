using Dicom;
using System.IO;

namespace Metadata_extractor
{
    public interface IDicomMetadataExtractor
    {
        void ExtractMetadata(DicomFile dicomFile, FileStream outputFile);
    }
}
