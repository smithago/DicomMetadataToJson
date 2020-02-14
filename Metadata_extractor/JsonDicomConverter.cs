using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Dicom;
using Dicom.IO.Buffer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Metadata_extractor
{
    /// <summary>
    /// Converts a DicomDataset object to and from JSON using the NewtonSoft Json.NET library
    /// </summary>
    public class JsonDicomConverter : JsonConverter
    {
        private readonly bool _writeTagsAsKeywords;
        private readonly static Encoding _jsonTextEncoding = Encoding.UTF8;
        private readonly JsonLoadSettings _jsonLoadSettings;

        /// <summary>
        /// Initialize the JsonDicomConverter.
        /// </summary>
        /// <param name="writeTagsAsKeywords">Whether to write the json keys as DICOM keywords instead of tags. This makes the json non-compliant to DICOM JSON.</param>
        public JsonDicomConverter(bool writeTagsAsKeywords = false)
            : this(writeTagsAsKeywords, new JsonLoadSettings())
        {
        }

        public JsonDicomConverter(bool writeTagsAsKeywords, JsonLoadSettings jsonLoadSettings)
        {
            _writeTagsAsKeywords = writeTagsAsKeywords;
            _jsonLoadSettings = jsonLoadSettings;
        }

        #region JsonConverter overrides

        /// <summary>
        /// Writes the JSON representation of the object.
        /// </summary>
        /// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter"/> to write to.</param>
        /// <param name="value">The value.</param>
        /// <param name="serializer">The calling serializer.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var dataset = (DicomDataset)value;

            writer.WriteStartObject();
            foreach (var item in dataset)
            {
                if (((uint)item.Tag & 0xffff) == 0)
                {
                    // Group length (gggg,0000) attributes shall not be included in a DICOM JSON Model object.
                    continue;
                }

                // Unknown or masked tags cannot be written as keywords
                var unknown = item.Tag.DictionaryEntry == null
                              || string.IsNullOrWhiteSpace(item.Tag.DictionaryEntry.Keyword)
                              ||
                              (item.Tag.DictionaryEntry.MaskTag != null &&
                               item.Tag.DictionaryEntry.MaskTag.Mask != 0xffffffff);

                if(unknown)
                {
                    continue;
                }
                if (_writeTagsAsKeywords)
                {
                    writer.WritePropertyName(item.Tag.DictionaryEntry.Keyword);
                }
                else
                {
                    writer.WritePropertyName(item.Tag.Group.ToString("X4") + item.Tag.Element.ToString("X4"));
                }

                WriteJsonDicomItem(writer, item, serializer);
            }
            writer.WriteEndObject();
        }

        #endregion


    #region WriteJson helpers

        private void WriteJsonDicomItem(JsonWriter writer, DicomItem item, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("vr");
            writer.WriteValue(item.ValueRepresentation.Code);
            

            switch (item.ValueRepresentation.Code)
            {
                case "PN":
                    WriteJsonPersonName(writer, (DicomPersonName)item);
                    break;
                case "SQ":
                    WriteJsonSequence(writer, (DicomSequence)item, serializer);
                    break;
                case "OB":
                case "OD":
                case "OF":
                case "OL":
                case "OV":
                case "OW":
                case "UN":
                    break;
                case "FL":
                    WriteJsonElement<float>(writer, (DicomElement)item);
                    break;
                case "FD":
                    WriteJsonElement<double>(writer, (DicomElement)item);
                    break;
                case "IS":
                case "SL":
                    WriteJsonElement<int>(writer, (DicomElement)item);
                    break;
                case "SS":
                    WriteJsonElement<short>(writer, (DicomElement)item);
                    break;
                case "SV":
                    WriteJsonElement<long>(writer, (DicomElement)item);
                    break;
                case "UL":
                    WriteJsonElement<uint>(writer, (DicomElement)item);
                    break;
                case "US":
                    WriteJsonElement<ushort>(writer, (DicomElement)item);
                    break;
                case "UV":
                    WriteJsonElement<ulong>(writer, (DicomElement)item);
                    break;
                case "DS":
                    WriteJsonDecimalString(writer, (DicomElement)item);
                    break;
                case "AT":
                    WriteJsonAttributeTag(writer, (DicomElement)item);
                    break;
                default:
                    WriteJsonElement<string>(writer, (DicomElement)item);
                    break;
            }
            writer.WriteEndObject();
            
        }

        private void WriteJsonDecimalString(JsonWriter writer, DicomElement elem)
        {
            if (elem.Count != 0)
            {
                writer.WritePropertyName("Value");
                writer.WriteStartArray();
                foreach (var val in elem.Get<string[]>())
                {
                    if (string.IsNullOrEmpty(val))
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        var fix = FixDecimalString(val);
                        if (ulong.TryParse(fix, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong xulong))
                        {
                            writer.WriteValue(xulong);
                        }
                        else if (long.TryParse(fix, NumberStyles.Integer, CultureInfo.InvariantCulture, out long xlong))
                        {
                            writer.WriteValue(xlong);
                        }
                        else if (decimal.TryParse(fix, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal xdecimal))
                        {
                            writer.WriteValue(xdecimal);
                        }
                        else if (double.TryParse(fix, NumberStyles.Float, CultureInfo.InvariantCulture, out double xdouble))
                        {
                            writer.WriteValue(xdouble);
                        }
                        else
                        {
                            throw new FormatException($"Cannot write dicom number {val} to json");
                        }
                    }
                }
                writer.WriteEndArray();
            }
        }

        private bool IsValidJsonNumber(string val)
        {
            try
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Fix-up a Dicom DS number for use with json.
        /// Rationale: There is a requirement that DS numbers shall be written as json numbers in part 18.F json, but the
        /// requirements on DS allows values that are not json numbers. This method "fixes" them to conform to json numbers.
        /// </summary>
        /// <param name="val">A valid DS value</param>
        /// <returns>A json number equivalent to the supplied DS value</returns>
        private string FixDecimalString(string val)
        {
            if (IsValidJsonNumber(val))
            {
                return val;
            }

            if (string.IsNullOrWhiteSpace(val))
            { return null; }

            val = val.Trim();

            var negative = false;
            // Strip leading superfluous plus signs
            if (val[0] == '+')
            {
                val = val.Substring(1);
            }
            else if (val[0] == '-')
            {
                // Temporarily remove negation sign for zero-stripping later
                negative = true;
                val = val.Substring(1);
            }

            // Strip leading superfluous zeros
            if (val.Length > 1 && val[0] == '0' && val[1] != '.')
            {
                int i = 0;
                while (i < val.Length - 1 && val[i] == '0' && val[i + 1] != '.')
                {
                    i++;
                }

                val = val.Substring(i);
            }

            // Re-add negation sign
            if (negative)
            { val = "-" + val; }

            if (IsValidJsonNumber(val))
            {
                return val;
            }

            throw new ArgumentException("Failed converting DS value to json");
        }

        private void WriteJsonElement<T>(JsonWriter writer, DicomElement elem)
        {
            if (elem.Count != 0)
            {
                writer.WritePropertyName("Value");
                writer.WriteStartArray();
                foreach (var val in elem.Get<T[]>())
                {
                    if (val == null || (typeof(T) == typeof(string) && val.Equals("")))
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        writer.WriteValue(val);
                    }
                }
                writer.WriteEndArray();
            }
        }

        private void WriteJsonAttributeTag(JsonWriter writer, DicomElement elem)
        {
            if (elem.Count != 0)
            {
                writer.WritePropertyName("Value");
                writer.WriteStartArray();
                foreach (var val in elem.Get<DicomTag[]>())
                {
                    if (val == null)
                    { writer.WriteNull(); }
                    else
                    { writer.WriteValue(((uint)val).ToString("X8")); }
                }
                writer.WriteEndArray();
            }
        }

        private void WriteJsonSequence(JsonWriter writer, DicomSequence seq, JsonSerializer serializer)
        {
            if (seq.Items.Count != 0)
            {
                writer.WritePropertyName("Value");
                writer.WriteStartArray();

                foreach (var child in seq.Items)
                { WriteJson(writer, child, serializer); }

                writer.WriteEndArray();
            }
        }

        private void WriteJsonPersonName(JsonWriter writer, DicomPersonName pn)
        {
            if (pn.Count != 0)
            {
                writer.WritePropertyName("Value");
                writer.WriteStartArray();

                foreach (var val in pn.Get<string[]>())
                {
                    if (string.IsNullOrEmpty(val))
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        writer.WriteStartObject();
                        writer.WritePropertyName("Alphabetic");
                        writer.WriteValue(val);
                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndArray();
            }
        }

        #endregion
        
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns>
        /// <c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.
        /// </returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(DicomDataset).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

    }
}
