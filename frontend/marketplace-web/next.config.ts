import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  /*
   * Next writes AGENTS.md and CLAUDE.md at the project root on every dev run, as notes for an
   * agent reading this repository. We have one document for that, in docs/, and two generated
   * files that land in git as untracked noise nobody asked for.
   */
  agentRules: false,
};

export default nextConfig;
