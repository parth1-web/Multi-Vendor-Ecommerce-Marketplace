import { defineConfig, globalIgnores } from "eslint/config";
import nextCoreWebVitals from "eslint-config-next/core-web-vitals";
import nextTypeScript from "eslint-config-next/typescript";

/**
 * eslint-config-next 16 ships flat configs directly, so there is no FlatCompat bridge. The
 * project rule from the frontend architecture doc is enforced here rather than left to
 * discipline: a file that says "use client" has to be a client for a reason.
 */
export default defineConfig([
  ...nextCoreWebVitals,
  ...nextTypeScript,
  {
    rules: {
      // A client boundary without a reason is the failure mode this project cares about most:
      // it drags the whole component tree across the server boundary for nothing.
      "no-restricted-syntax": [
        "warn",
        {
          selector:
            'CallExpression[callee.name="require"][arguments.0.value=/^(\\.\\.\\/)*components\\/ui/]',
          message: "UI primitives are client components; import them from a client leaf instead.",
        },
      ],
    },
  },
  globalIgnores([".next/**", "out/**", "build/**", "node_modules/**", "next-env.d.ts"]),
]);
