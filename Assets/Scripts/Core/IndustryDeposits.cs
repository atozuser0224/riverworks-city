namespace Riverworks
{
    public interface IIndustryEnvironment
    {
        bool HasWater(int microX, int microZ);
        bool HasDeposit(Resource resource, int microX, int microZ);
    }

    public static class IndustryDeposits
    {
        public static Resource At(int cityX, int cityZ)
        {
            if (cityX == 16 && cityZ == 10) return Resource.CopperOre;
            if (cityX == 10 && cityZ == 16) return Resource.Coal;
            if (cityX == 16 && cityZ == 16) return Resource.Bauxite;
            if (cityX == 16 && cityZ == 4) return Resource.CrudeOil;
            return Resource.Coins;
        }

        public static bool IsSpecial(Resource resource, int cityX, int cityZ) =>
            resource != Resource.Coins && At(cityX, cityZ) == resource;
    }
}
