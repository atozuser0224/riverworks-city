using System;
using System.Collections.Generic;

namespace Riverworks
{
    /// <summary>Wire-safe resident facts. Field names intentionally match the gateway JSON contract.</summary>
    [Serializable]
    public sealed class CitizenDecisionFacts
    {
        public string id = "";
        public string name = "";
        public string persona = "";
        public string activity = "";
        public int home = -1;
        public int job = -1;
        public int current = -1;
        public List<CitizenAllowedDecision> allowed = new List<CitizenAllowedDecision>();
        public string recentMemory = "";
    }

    [Serializable]
    public sealed class CitizenDecisionBatch
    {
        public List<CitizenDecisionFacts> residents = new List<CitizenDecisionFacts>();
    }

    [Serializable]
    public sealed class CitizenAllowedDecision
    {
        public string intent = "";
        public int target = -1;

        public CitizenAllowedDecision() { }
        public CitizenAllowedDecision(string intentValue, int targetValue)
        {
            intent = intentValue;
            target = targetValue;
        }
    }

    /// <summary>One constrained resident decision returned by the gateway.</summary>
    [Serializable]
    public sealed class ResponseDecision
    {
        public string id = "";
        public string intent = "";
        public int target = -1;
        public float dwellSeconds;
        public string thought = "";
        public string memory = "";
        public string mood = "";
    }

    internal sealed class CitizenDecisionPlan
    {
        internal string Intent = "";
        internal int Target = -1;
        internal int Origin = -1;
        internal readonly List<int> Route = new List<int>();
        internal float DwellSeconds;
        internal float AgeSeconds;
        internal float ExpiresAfterSeconds;
    }
}
