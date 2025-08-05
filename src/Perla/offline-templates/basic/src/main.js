console.log("Welcome to Perla!");

export function setMessage(element) {
  element.innerHTML = `
  <h1>Perla Offline Template</h1>
  <p>This is a basic offline template for Perla.</p>
  <p>It is designed to work without any server-side processing.</p>
  <p>Feel free to modify it as per your requirements.</p>
  `;
}

document.addEventListener("DOMContentLoaded", () => {
  const appRoot = document.querySelector("app-root");
  if (appRoot) {
    const element = document.createElement("section");
    setMessage(element);
    appRoot.appendChild(element);
  } else {
    console.error("App root element not found.");
  }
});
