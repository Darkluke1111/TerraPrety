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

        protected override int chunkRange { get { return 2; } } //5 by 5 chunks

        public override double ExecuteOrder() {
            return 0.25; //After Deposits and before Worldgen Structures
        }

        public override bool ShouldLoad(EnumAppSide side) {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api) {
            base.StartServerSide(api);

            if (TerraGenConfig.DoDecorationPass) {
                api.Event.GetWorldgenBlockAccessor(OnWorldGenBlockAccessor);
                api.Event.InitWorldGenerator(initWorldGen, "superflat");
                api.Event.MapRegionGeneration(OnMapChunkGen, "standard");
                api.Event.MapRegionGeneration(OnMapChunkGen, "superflat");
                api.Event.ChunkColumnGeneration(GenChunkColumn, EnumWorldGenPass.TerrainFeatures, "standard");
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
        public void OnMapChunkGen(IMapRegion mapRegion, int regionX, int regionZ, ITreeAttribute chunkGenParams = null) {

        }

        //Actually generate the Rivers for these chunks here!
        public override void GeneratePartial(IServerChunk[] chunks, int chunkX, int chunkZ, int basePosX, int basePosZ) {
            
        }

        /*
         * Planning Notes!
         * 
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
         */
    }
}
