using Assets.Scripts.Networking;
using LaunchPadBooster.Networking;

namespace PowerTransmitterPlus
{
    // Server -> client message: pushes the host's beam visual config so all
    // clients render beams with the same appearance. Sent on connect and
    // whenever the host changes any visual setting.
    public class BeamVisualConfigMessage : INetworkMessage
    {
        public float BeamWidth;
        public string BeamColorHex;
        public float EmissionIntensity;
        public float StripeWavelength;
        public float ScrollSpeed;

        public void Serialize(RocketBinaryWriter writer)
        {
            writer.WriteSingle(BeamWidth);
            writer.WriteString(BeamColorHex ?? "000DFF");
            writer.WriteSingle(EmissionIntensity);
            writer.WriteSingle(StripeWavelength);
            writer.WriteSingle(ScrollSpeed);
        }

        public void Deserialize(RocketBinaryReader reader)
        {
            BeamWidth = reader.ReadSingle();
            BeamColorHex = reader.ReadString();
            EmissionIntensity = reader.ReadSingle();
            StripeWavelength = reader.ReadSingle();
            ScrollSpeed = reader.ReadSingle();
        }

        public void Process(long hostId)
        {
            if (NetworkManager.IsServer) return;
            BeamVisualConfigSync.OnHostConfigReceived(this);
        }
    }
}
