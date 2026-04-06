namespace Core.Session;

public enum ClientState
{
    NotConnected = 0x00,
    Connecting = 0x01,
    CharScreen = 0x02,
    WorldEnter = 0x03,
    Playing = 0x04,
    LinkDead = 0x05,
    Disconnected = 0x06
};