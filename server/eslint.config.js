import js from "@eslint/js";
import tseslint from "typescript-eslint";

const typedFiles = ["**/*.ts", "**/*.tsx", "**/*.mts", "**/*.cts"];
const recommendedTypeChecked = tseslint.configs.recommendedTypeChecked.map((config) => ({
  ...config,
  files: config.files ?? typedFiles
}));

export default tseslint.config(
  { ignores: ["dist/**", "node_modules/**", "coverage/**"] },
  js.configs.recommended,
  {
    files: ["backup/**/*.mjs"],
    languageOptions: { globals: { process: "readonly", console: "readonly" } }
  },
  {
    files: ["src/admin/views/*.js"],
    languageOptions: {
      globals: {
        document: "readonly",
        fetch: "readonly",
        FormData: "readonly",
        location: "readonly"
      }
    }
  },
  ...recommendedTypeChecked,
  {
    files: typedFiles,
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname
      }
    }
  },
  {
    files: ["test/**/*.ts", "scripts/**/*.ts"],
    extends: [tseslint.configs.disableTypeChecked]
  }
);
