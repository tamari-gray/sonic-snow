using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Attach to the username panel (or anywhere convenient) and assign both fields.
// Hides the Play button while the input field is empty, shows it once at least
// one character has been typed.
public class UsernameInputValidator : MonoBehaviour
{
    [SerializeField] private TMP_InputField usernameInputField;
    [SerializeField] private Button playButton;

    void OnEnable()
    {
        if (usernameInputField != null)
            usernameInputField.onValueChanged.AddListener(UpdatePlayButtonVisibility);

        UpdatePlayButtonVisibility(usernameInputField != null ? usernameInputField.text : "");
    }

    void OnDisable()
    {
        if (usernameInputField != null)
            usernameInputField.onValueChanged.RemoveListener(UpdatePlayButtonVisibility);
    }

    private void UpdatePlayButtonVisibility(string currentText)
    {
        if (playButton == null) return;

        playButton.gameObject.SetActive(!string.IsNullOrEmpty(currentText));
    }
}