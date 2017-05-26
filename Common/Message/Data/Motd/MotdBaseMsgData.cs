﻿using LunaCommon.Message.Base;
using LunaCommon.Message.Types;
using System;

namespace LunaCommon.Message.Data.Motd
{
    public class MotdBaseMsgData : MessageData
    {
        public override ushort SubType => (ushort)(int)MotdMessageType;

        public virtual MotdMessageType MotdMessageType => throw new NotImplementedException();
    }
}
