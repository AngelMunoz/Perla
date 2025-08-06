// @ts-ignore
import { html } from "lit";
import { MyButton } from "@nested/button.js";
import { MyMenu } from "@nested/menu.js";

export function SamplePage() {
  const menuItems = [
    { label: "Home", href: "/" },
    { label: "About", href: "/about" },
    { label: "Contact", href: "/contact" },
  ];

  const handleButtonClick = () => {
    console.log("Button clicked!");
  };

  return html`
    <div>
      ${MyMenu({ menuItems })}
      <div style="margin: 1rem;">
        ${MyButton({ label: "Click Me", onClick: handleButtonClick })}
      </div>
    </div>
  `;
}
