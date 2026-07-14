using System;
using UnityEngine;

namespace Fusion.Addons.Physics
{
  public partial class NetworkRigidbody<RBType, PhysicsSimType> {

    /// <inheritdoc/>
    public override void Teleport(Vector3? position = null, Quaternion? rotation = null) {
      // If the object is just interpolating as a proxy, cannot teleport.
      // If the object is being simulated and predicted with mode SimulateAlways, allow teleport.
      if (Object.IsInSimulation == false) {
        return;
      }
      
      if (position.HasValue) {
        _transform.position     = position.Value;
        RBPosition             = position.Value;
        Data.TRSPData.Position = position.Value;
      }
      
      if (rotation.HasValue) {
        _transform.rotation     = rotation.Value;
        RBRotation          = rotation.Value;
        if (UsePreciseRotation) {
          Data.FullPrecisionRotation = rotation.Value;
        } else {
          Data.TRSPData.Rotation     = rotation.Value;
        }
      }

      // Keeping the key well under 1 byte in size
      var key = Math.Abs(Data.TRSPData.TeleportKey) + 1;
      if (key > 30) {
        key = 1;
      }
      
      Data.TRSPData.TeleportKey = key;
    }
  }
}
