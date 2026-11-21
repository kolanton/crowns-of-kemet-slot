using UnityEngine;
using UnityEngine.UI;

namespace EgyptianSlot
{
    /// <summary>
    /// Wires the buttons and readouts to the machine. Holds no game state of its own:
    /// every value is read back from the controller on <see cref="Refresh"/>.
    ///
    /// The arrangement follows the pack's own promotional mock-up — stake on the left,
    /// spin in the middle, wallet on the right — since that is the layout the background
    /// art was composed around.
    /// </summary>
    public class SlotHud : MonoBehaviour
    {
        [Header("Readouts")]
        [SerializeField] private Text _betLabel;
        [SerializeField] private Text _levelLabel;
        [SerializeField] private Text _lineBetLabel;
        [SerializeField] private Text _balanceLabel;
        [SerializeField] private Text _messageLabel;

        [Header("Controls")]
        [SerializeField] private Button _spinButton;
        [SerializeField] private Button _levelUpButton;
        [SerializeField] private Button _levelDownButton;
        [SerializeField] private Button _maxBetButton;
        [SerializeField] private Button _autoplayButton;
        [SerializeField] private Text _autoplayLabel;
        [SerializeField] private Button _resetButton;

        [Header("Spin button")]
        [SerializeField] private Image _spinImage;
        [SerializeField] private Text _spinLabel;

        private SlotMachineController _controller;

        /// <summary>Called once by the controller, which owns the lifetime of the pair.</summary>
        public void Bind(SlotMachineController controller)
        {
            _controller = controller;

            if (_spinButton != null) _spinButton.onClick.AddListener(controller.Spin);
            if (_levelUpButton != null) _levelUpButton.onClick.AddListener(() => controller.StepLineBet(1));
            if (_levelDownButton != null) _levelDownButton.onClick.AddListener(() => controller.StepLineBet(-1));
            if (_maxBetButton != null) _maxBetButton.onClick.AddListener(controller.MaxBet);
            if (_autoplayButton != null) _autoplayButton.onClick.AddListener(controller.ToggleAutoplay);
            if (_resetButton != null) _resetButton.onClick.AddListener(controller.ResetBalance);

            Refresh();
        }

        private void Update()
        {
            // Space and Return are what people reach for in a browser build.
            if (_controller == null) return;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                _controller.Spin();
        }

        /// <summary>Pulls every displayed value back from the controller.</summary>
        public void Refresh()
        {
            if (_controller == null) return;

            bool spinning = _controller.IsSpinning;
            bool atTop = _controller.Level >= _controller.LevelCount;
            bool atBottom = _controller.Level <= 1;

            if (_betLabel != null) _betLabel.text = _controller.TotalBet.ToString("N0");
            if (_levelLabel != null) _levelLabel.text = _controller.Level.ToString();
            if (_lineBetLabel != null) _lineBetLabel.text = _controller.LineBet.ToString("N0");
            if (_balanceLabel != null) _balanceLabel.text = _controller.Balance.ToString("N0");

            if (_spinButton != null) _spinButton.interactable = _controller.CanSpin();
            if (_levelUpButton != null) _levelUpButton.interactable = !spinning && !atTop;
            if (_levelDownButton != null) _levelDownButton.interactable = !spinning && !atBottom;
            if (_maxBetButton != null) _maxBetButton.interactable = !spinning && !atTop;
            if (_resetButton != null) _resetButton.interactable = !spinning;

            if (_spinLabel != null) _spinLabel.text = spinning ? "" : "SPIN";
            if (_spinImage != null)
            {
                // Dim the disc while it cannot be pressed, so the button's state is visible
                // without a second piece of art.
                float tint = _controller.CanSpin() ? 1f : 0.45f;
                _spinImage.color = new Color(tint, tint, tint, 1f);
            }

            if (_autoplayLabel != null)
                _autoplayLabel.text = _controller.AutoplayEnabled ? "STOP\nAUTO" : "AUTO\nPLAY";

            if (_messageLabel == null) return;

            bool broke = !spinning && _controller.Balance < _controller.TotalBet;
            _messageLabel.text = broke
                ? "Out of coins — press RESET"
                : spinning ? string.Empty : "Press SPIN or hit Space";
        }
    }
}
