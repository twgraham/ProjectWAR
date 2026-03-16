using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Character templates response sent by the server in response to F_REQUEST_CHAR_TEMPLATES.
/// Currently, unsure how templates operate in the client. Binary suggests something like the following
/// Wire format:
/// uint32_t    nameCount;
/// TemplateName names[nameCount];      // nameCount * 28 bytes
///
/// uint32_t    classCount;             // == nameCount
/// uint32_t    classes[classCount];    // big-endian uint32 each
///
/// uint32_t    raceCount;              // == nameCount
/// uint32_t    races[raceCount];       // big-endian uint32 each
///
/// uint32_t    genderCount;            // == nameCount
/// uint32_t    genders[genderCount];   // big-endian uint32 each
/// </summary>
public class CharacterTemplatesResponse
{
    [FixedLength(16)]
    public byte[] EmptyTemplates { get; set; } = new byte[12];
}