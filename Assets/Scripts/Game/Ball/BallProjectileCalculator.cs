using UnityEngine;

public class BallProjectileCalculator : MonoBehaviour
{
    Transform _hitSprite;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _hitSprite = GameObject.Find("Point").transform;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public Vector3 Calculate(Vector3 vel, float mass, LineRenderer line)
    {
        int cnt = 128;

        line.positionCount = cnt;
        Vector3 pos = transform.position;
        //Vector3 velocity = vel / mass;
        Vector3 velocity = vel;

        float time = 0.1f;
        line.SetPosition(0, pos);

        for(int i = 1; i < cnt; i++)
        {
            Vector3 point = pos + time * velocity;
            point.y += (Physics.gravity.y * 0.5f * time * time);
            line.SetPosition(i, point);
            time += 0.1f;

            Vector3 last = line.GetPosition(i - 1);
            if(Physics.Raycast(last, (point - last).normalized, out RaycastHit hit, (point - last).magnitude)) {
                line.SetPosition(i, hit.point);
                line.positionCount = i + 1;

                Vector3 p = hit.point;
                p.y += 0.001f;

                if (hit.collider.tag.Equals("Finish")) return p;

                _hitSprite.gameObject.SetActive(true);
                _hitSprite.position = p;

                return p;
            }
        }
        return Vector3.zero;
    }

    public void init()
    {
        _hitSprite.gameObject.SetActive(false);
    }
}
