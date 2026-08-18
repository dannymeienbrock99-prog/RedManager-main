import "./styles.css";
import App from "./App.svelte";

const target = document.getElementById("app");
if (!target) {
  throw new Error('The application mount element "#app" was not found.');
}

const app = new App({
  target,
});

export default app;
