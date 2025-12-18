using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class GenRivers : GenPartial {

        ICoreServerAPI Sapi;
        IWorldGenBlockAccessor blockAccessor;
        LCGRandom riverRand;
        int regionSize;

        public int NoiseSizeRivers;
        public int NoiseSizeCoast;
        public MapLayerBase CoastMap;
        public MapLayerBase RiverMap;

        protected override int chunkRange { get { return 2; } } //5 by 5 chunks

        public override double ExecuteOrder() {
            return 0.25; //After Deposits and before Caves + Worldgen Structures
        }

        public override bool ShouldLoad(EnumAppSide side) {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api) {
            base.StartServerSide(api);
            Sapi = api;

            if (TerraGenConfig.DoDecorationPass) {
                api.Event.GetWorldgenBlockAccessor(OnWorldGenBlockAccessor);
                api.Event.MapRegionGeneration(OnMapRegionGen, "standard");
                api.Event.ChunkColumnGeneration(GenChunkColumn, EnumWorldGenPass.Terrain, "standard");
            }
        }

        private void OnWorldGenBlockAccessor(IChunkProviderThread chunkProvider) {
            blockAccessor = chunkProvider.GetBlockAccessor(true);
        }

        public override void initWorldGen() {
            base.initWorldGen();

            riverRand = new LCGRandom(api.WorldManager.Seed + 4528);
            regionSize = api.WorldManager.RegionSize;
        }

        //Set up the river overall layout for the full Continental Region, then plot out the river nodes for this region.
        public void OnMapRegionGen(IMapRegion mapRegion, int regionX, int regionZ, ITreeAttribute chunkGenParams = null) {
            
        }

        //Actually generate the Rivers for these chunks here!
        public override void GeneratePartial(IServerChunk[] chunks, int chunkX, int chunkZ, int basePosX, int basePosZ) {
            
        }

        public void PassCoastMapTheLandforms(float[][] thresholds, NewNormalizedSimplexFractalNoise terrainNoiseDuplicate) {
            ((CoastMap)CoastMap).SetThresholdAndNoise(thresholds, terrainNoiseDuplicate);
        }

        public void InitWorldGenPostGenMaps(ICoreServerAPI sapi) {
            NoiseSizeCoast = sapi.WorldManager.RegionSize / TerraGenConfig.landformMapScale;
            NoiseSizeRivers = sapi.WorldManager.RegionSize / TerraGenConfig.landformMapScale;
            var genMaps = sapi.ModLoader.GetModSystem<GenMaps>();

            CoastMap = new CoastMap(sapi.WorldManager.Seed + 1873, NoiseSizeCoast, sapi); //Seed probably doesn't matter for this.
            RiverMap = new RiverMap(sapi.WorldManager.Seed + 1873, NoiseSizeRivers, sapi, genMaps.requireLandAt);

            var genTerraPrety = api.ModLoader.GetModSystem<GenTerraPrety>();
            genTerraPrety.InitGenTerraPretyLandforms();
        }

        public void OnMapRegionGenPostGenMaps(IMapRegion mapRegion, int regionX, int regionZ) {
            var coastPad = 1;
            var riverPad = 1;
            var oceanMap = mapRegion.OceanMap;
            var oceanPad = oceanMap.BottomRightPadding;
            var landformMap = mapRegion.LandformMap;
            var genTerraPrety = Sapi.ModLoader.GetModSystem<GenTerraPrety>();
            var landLerpMap = genTerraPrety.GetOrLoadLerpedLandformMapFromRegion(mapRegion, regionX, regionZ);
            var heightMap = mapRegion.ModMaps["LandformHeightMap"];

            var CoastalRegion = new IntDataMap2D { //It seems any map with a pad of 1 just doesn't set the TopLeft padding in GenMaps. Sticking with that?
                //Data = coastData,
                Size = NoiseSizeCoast + 1,
                //TopLeftPadding = coastPad,
                BottomRightPadding = coastPad
            };
            ((CoastMap)CoastMap).SetCoastAndLandformMaps(oceanMap, landLerpMap, landformMap.InnerSize, CoastalRegion.InnerSize);
            var coastData = CoastMap.GenLayer(regionX * NoiseSizeCoast, regionZ * NoiseSizeCoast, NoiseSizeCoast + coastPad, NoiseSizeCoast + coastPad);
            CoastalRegion.Data = coastData;

            mapRegion.ModMaps["TerraPretyCoastMap"] = CoastalRegion;

            var RiverRegion = new IntDataMap2D {
                Size = NoiseSizeRivers + 1,
                BottomRightPadding = riverPad
            };
            ((RiverMap)RiverMap).SetMapsAndSizesFromRegion(CoastalRegion, heightMap, RiverRegion.InnerSize, oceanPad, new XZ(regionX, regionZ));
            var riverData = RiverMap.GenLayer(regionX * NoiseSizeRivers, regionZ * NoiseSizeRivers, NoiseSizeRivers + riverPad, NoiseSizeRivers + riverPad);
            RiverRegion.Data = riverData;

            mapRegion.ModMaps["TerraPretyRiverMap"] = RiverRegion;
        }

        /*
         * Planning Notes! - The Go-Ahead for actually Raising/Lowering Land as required to fit the river has been given, woo!
         * 
         * - River Goals -
         *   - The rivers will flow from high to low, and not have a bump in the middle
         *   - We can try to fit it alongside the existing world generation and landforms. This won’t be strict though, more an inclination to follow this pattern.
         *     - Where a cliff Landform spawns is where a Waterfall would happen
         *   - There can be a few branches on each River, probably smaller in scale overall compared to the Rivers Mod, so throw out any preconceived notions from that mod.
         *   - Ideally they end in the ocean. Make it look relatively smooth of a transition.
         *     - Perhaps build a Delta when we need to raise up the ocean floor?
         *     - Rivers flowing into Lakes and then flowing back into the River again to places like a Wetlands or wet area as another possible feature?
         *     - A big portion of the River will likely remain at or around Sea Level, as when we deviate from that point, it becomes harder and harder to keep it together without ending it suddenly.
         * 
         * - Possible Drawbacks of this Method -
         *   - Going to have to entirely wrest control of GenTerra’s main Generation with a Patch or replacing the ModSystem most likely. I kinda gotta just take over that for this to be the most efficient.
         *     - Maybe later it can be translated into patches but, oof, that will be complex
         *     - Just means a bit of work if Vanilla changes up GenTerra significantly. But I plan on just adding to it - not removing where I can.
         *   - Ideally this will still be compatible with other Landform mods potentially, but that’s not a focused goal to maintain that.
         *     - Currently worst I see will be either boring or just weird gen if used with other Landforms - but simply put, not our problem, not our goal.
         *   - Given the need to be able to raise/lower terrain when needed for it to make sense, the shape of continents may adjust some when Rivers are involved. Can be minimized though, but will take iterations.
         *     - Goal will be to minimize this as much as possible, but impossible to get it perfect. Just by virtue of what it must do to even function.
         * 
         * -- To Generate A River --
         *   - Requires a few data maps:
         *     - OceanMap for Oceanicity Levels
         *     - LandformMap for Landform Data
         *     - LandformHeightmap to compare relative expected zones
         *     - Hopefully this can be all sent to the RiverMap generation so that it's relatively easy and 
         *   - Where a River is, it MUST generate land that is relatively above water.
         *     - Oceanicity should be 0 exactly where the River exists.
         *       - The further out from the river, the more Oceanicity returns to it's normal value again.
         *       - This ensures there is always realistic land and it will be relatively blended together most likely.
         *     - The Landform Heightmap can serve as a relative guess about the general expected height of a zone.
         *       - Will not be accurate until after GenTerra, but at that point based on the main requirement above, there WILL be land here (or else)
         *       - Based on the actual y-height of an area, RNG can take effect and add in various possible 'Segments' of a river that make sense and fit in.
         *         - If an area is an expected source of a River, the Landform Heightmap is high but the TerrainNoise results in a flat Sealevel area? Lets make a Lake source.
         *         - If it ends up in a mountain, it can start as many mountain streams instead.
         *         - Variety is possible!
         *   - A system of Landform Tags of sorts would help extremely.
         *     - Build lists of landforms that have a specific tag
         *     - This can be used to check and compare on the fly what we can expect from any landform for the purposes of Building the Rivers.
         *       - For the majority of the primary River, stick to lower Landform Heightmap areas that have the flat tag to start, and moving into 'no tag' territory is fine but should prove a greater hit/risk to the River Gen pattern, weight it lighter over the better options.
         *       - Tributaries and offshoots or nearing the end of the Primary River can be more risky as they are expected to end sooner then the Primary.
         *         - Perhaps a Risk-Factor based on the "life" that still exists in the River?
         *         - Higher "Life" means take less risks, a River close to "end" can be more risky!
         *     - This in conjunction with the Landform Heightmap can ideally build the rivers to follow the general terrain shape that would exist beforehand.
         *       - Less land needs to be raised/lowered this way! Hopefully!
         *   
         *   - To find a starting point, it is likely best to start from the Ocean and work inwards. That way it is easy to have many branching tributaries.
         *     - Possibly iterate over the map area, find points that have an oceanicity between a threshold (configurable?) and a Landform Heightmap location of lower mids or less (also configurable?)
         *     - After a potential point is found, attempt to roll for a chance for a River
         *       - If it succeeds, add it to the River start list and then prevent another river from starting within a (configurable) range around it
         *     - Then run through each successful point (potential for an additional verification step here if needed?) and create a river headed towards the middle
         *     - Possibly able to attempt a skip of the outer-most regions to trim down the search area, the outer-most ones are bound to LIKELY be deep ocean so, have a configurable value for this?
         *   
         * 
         * 
         * -- Below here is possibly out of date. Need to re-verify --
         * - Plan the River Map after GenMaps but before GenTerra and actually do the Gen of the Rivers after GenTerra does it's job.
         *   - The Highest point of each collumn has been set
         *   - Water has been generated for anything below Sea Level
         *   - The overall shape of the world has been determined, so height-tracking and detecting is possible
         *   - Likely still a very good idea to limit River Gen to a configurable amount per continent
         * 
         * - Generate rivers from Sink to Source? IE from Ocean to somewhere inland
         *   - Would mean it's easy to create branching tributary rivers! Those would be nice.
         *   - Starting at the Cell Walls and mid to low heightmap points to ensure almost guaranteed output in the sea.
         *     - Oceanicity > 60 (on the visualizer) probably as well?
         *   - Have to avoid collisions with others. Shouldn't be too bad if segments are built using a tag and coordinate system?
         *     - Should they collide, cancel a tributary or see about ending it early? MAYBE consider merging them into one source stream, even if it's unrealistic.
         *   - If it ends and not at the most ideal situation, eeh. Have a few possible 'ends' like lakes or pond sources. Maybe just breaking apart into just fragments of small source streams. If close to a lake or mountain, waterfall source from mountain stream?
         * 
         * - Each River and Segment has a few variables
         *   - River ID: Based on the Continent's centerpoint location and the order of generation for the river itself
         *   - List of the segments that compose this river (River only of course)
         *   - Water Level (Segments): Determines how deep/wide the river gets, as it goes further inland this shrinks and once it hits 0, the river ends.
         *     - If it has to end early, perhaps try and make a lake or pond as a source.
         *   - Start and End points: The middle of the river itself, which the Water Level will change the width. For the full river, it's the source and drain of the river, for the segments it's the points defining the start and end of that segment.
         *   - References up and downstream (Segments) from this point, just incase it needs to change anything or line things up better
         *   - Reference back to the parent river! (Segments)
         *   - Direction Vector? (Segments)
         *   - Possible 'Pattern' code? (Segments) To match up a few segments to build a specific pattern.
         * 
         * - When actually generating the river:
         *   - To prevent oddities with RNG and preventing the direction the River is explored from changing how it looks, try and keep everything pre-determined in the map river paths
         *     - Or just be certain to re-seed the RNG each time.
         *   - Some things have to be flexible somewhat, just a little, but the big ones are if suddenly it has to end early in either direction, lop off the remaining nodes.
         *     - Ideally should only have to be done if it turns out the land shape requires it by accident
         *     
         * 
         * - Approach -
         *   - First attempt to build a map that determines Sea, Coast and Inland -- Okay this has to be approached differently. A Map is not going to work here.
         *     - Needs the Terrain Height, Oceanicity from the OceanMap, and a radius around it to check and compare with.
         *     - Must happen after GenTerra, so ideally the existing water can be changed accordingly... I rather not completely bypass that chunk of GenTerra
         *       - Perhaps it is possible to Transpile out the Water stuff, leaving a completely dry world. Then add in the water in a second pass, 'The Flooding'?
         *       - Could be more efficient to let original GenTerra just fill in everything with 0 Oceanicity, or below a threshold like the 42 or 10 Oceanicity, then anything
         *         above is not filled in yet and left for the second pass? No matter what a second run through everything is going to be required cause we need the land fully constructed...
         *     - Can be done after GenTerra, to fill in seawater and freshwater properly...
         *       - But this means that the chunk IS generated, and only after this do we know for sure where the coast lies.
         *     
         *     - Will likely need to inject it after GenMaps and before GenTerra, as Water Type is determined during GenTerra.
         *     - Will need to patch GenTerra to pull from this OceanicityMap instead of comparing Oceanicity only.
         *     - Solves both the SeaWater problem and provides a launching point for RiverGen
         *   - Then this OceanicityMap can be utilized to pick from starting points for the Sinks of the Rivers
         *     - Will have to generate the full continental river region map at once to ensure that they are complete and generation direction will not matter.
         *     - After GenTerra to carve out the rivers from the world, and allowing for them to follow the ground-height and account for it.
         *       - Will need to provide this various map data to the RiverMap proper
         *     - Try to meander to the same relative height, or above, RARELY going down MINIMALLY to just allow for rivers digging deeper, and creating bigger banks before that point.
         *       This can reverberate down the river nodes to ensure they remain under the average ground height.
         *   - As the nodes are plotted, since we can assume we have the relative heights of the ground at this point, can have random variation nodes to pick from
         *     - If it hits a cliff or a sudden spike, attempt a Waterfall generation.
         *     - Forks in the river can occur in flat regions?
         */
    }
}
