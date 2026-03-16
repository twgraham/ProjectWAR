using Core.Infrastructure.Cryptography;
using FrameWork;

namespace WorldServer.NetWork.Crypt
{
    [Crypt("RC4Crypto")]
    public class RC4Crypto : ICryptHandler
    {
        public void Crypt(CryptKey key, byte[] packet, int offset, int len)
        {
            
            MythicRc4.Encrypt(key.GetbKey(), packet, offset, len);
        }

        public void Decrypt(CryptKey key, byte[] packet, int offset, int len)
        {
            MythicRc4.Decrypt(key.GetbKey(), packet, offset, len);
        }

        public CryptKey GenerateKey(BaseClient client)
        {
            return new CryptKey(new byte[0]);
        }
    }
}