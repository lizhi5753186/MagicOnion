﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MagicOnion.Generator
{
    public partial class RegisterTemplate
    {
        public string Namespace { get; set; }
        public bool UnuseUnityAttribute { get; set; }
        public MagicOnion.CodeAnalysis.InterfaceDefintion[] Interfaces { get; set; }
    }
}