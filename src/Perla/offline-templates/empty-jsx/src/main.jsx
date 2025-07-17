import { render } from "preact";

function App() {
  return (
    <div>
      <h1>Welcome to the Preact App!</h1>
      <p>This is a basic template using Preact.</p>
      <p>Feel free to modify it as per your requirements.</p>
    </div>
  );
}

render(<App />, document.querySelector("app-root"));
