import { relative } from 'node:path';
import { defineConfig, type Plugin } from 'vitest/config';

/**
 * Makes `ng test` work when the checkout path contains a glob metacharacter.
 *
 * `@angular/build:unit-test` discovers the spec files itself and hands Vitest the result as
 * `test.include` - as *absolute* paths. Vitest does not treat that list as literal filenames; it
 * feeds it to tinyglobby as glob patterns. So every character of the absolute checkout path is
 * parsed as glob syntax, and a directory whose name contains `(`, `)`, `[`, `]`, `{`, `}`, `!` or
 * `*` turns into a group/class that does not match the literal directory. Discovery then finds
 * nothing, and Vitest exits 1 with "No test files found" while printing the very path it just
 * failed to match.
 *
 * Reproduced with the same sources, the same node_modules and the same angular.json: from
 * `C:/hopper-fe-check` the suite runs, from `C:/Users/.../files (7)/HOPPER/...` it finds zero files.
 *
 * A path relative to the project root has none of the checkout's directory names in it, so
 * rewriting the include list is enough to sidestep the whole class of problem. `enforce: 'post'`
 * puts this after `angular:vitest-configuration`, which is what populates the list. Mutating the
 * config in place rather than returning a partial one is deliberate: Vite deep-merges returned
 * config and would concatenate the two arrays, leaving the unmatched absolute entries behind.
 */
const relativizeTestInclude: Plugin = {
  name: 'hopper:relativize-test-include',
  enforce: 'post',
  config(config) {
    const include = config.test?.include;
    const root = config.root;
    if (!Array.isArray(include) || !root) {
      return;
    }

    for (let i = 0; i < include.length; i++) {
      const pattern = include[i];
      const asRelative = relative(root, pattern);
      // Only rewrite entries that really are absolute paths inside the project. Anything else is
      // a pattern the builder meant literally, and `relative()` would answer with `../` noise.
      if (asRelative && !asRelative.startsWith('..')) {
        include[i] = asRelative.replaceAll('\\', '/');
      }
    }
  },
};

export default defineConfig({
  plugins: [relativizeTestInclude],
});
